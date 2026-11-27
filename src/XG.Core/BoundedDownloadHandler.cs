// SPDX-License-Identifier: MIT
using System.Net;
namespace XG.Core;

/// <summary>连接层预算覆盖加载器等第三方安装器的内部请求；与文件门控分层，避免重复获取同一名额造成死锁。</summary>
internal sealed class BoundedDownloadHandler(HttpMessageHandler inner, Func<int> maximum, Func<string,CancellationToken,Task> cooldown) : DelegatingHandler(inner)
{
    private readonly object sync=new();
    private readonly Queue<object> waiting=[];
    private TaskCompletionSource changed=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int active;
    internal int Active { get { lock(sync)return active; } }
    private void Wake(){var old=changed;changed=new(TaskCreationOptions.RunContinuationsAsynchronously);old.TrySetResult();}
    private void Release(){lock(sync){active--;Wake();}}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
    {
        await cooldown(request.RequestUri!.Host,token);
        var ticket=new object();lock(sync)waiting.Enqueue(ticket);
        try {
            while(true) { token.ThrowIfCancellationRequested();Task signal;
                lock(sync){if(ReferenceEquals(waiting.Peek(),ticket)&&active<maximum()){waiting.Dequeue();active++;Wake();break;}signal=changed.Task;}
                await signal.WaitAsync(token);
            }
        }catch{lock(sync){var rest=waiting.Where(x=>!ReferenceEquals(x,ticket)).ToArray();waiting.Clear();foreach(var item in rest)waiting.Enqueue(item);Wake();}throw;}
        try {
            var response=await base.SendAsync(request,token);
            // 直到正文被消费/释放才归还连接名额，不能在收到响应头后提前放行。
            response.Content=new LeasedContent(response.Content,Release);
            return response;
        }catch{Release();throw;}
    }
    // GetStreamAsync调用方通常只释放流，不释放响应对象；流到达EOF或释放时也必须归还名额。
    private sealed class LeasedStream(Stream inner,Action leave):Stream
    {
        private Action? release=leave;
        private int Finish(int bytes){if(bytes==0)Interlocked.Exchange(ref release,null)?.Invoke();return bytes;}
        public override bool CanRead=>inner.CanRead;public override bool CanSeek=>inner.CanSeek;public override bool CanWrite=>inner.CanWrite;
        public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override int Read(byte[] buffer,int offset,int count)=>Finish(inner.Read(buffer,offset,count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)=>Finish(await inner.ReadAsync(buffer,token));
        public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token)=>ReadAsync(buffer.AsMemory(offset,count),token).AsTask();
        public override void Flush()=>inner.Flush();public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override void SetLength(long value)=>inner.SetLength(value);public override void Write(byte[] buffer,int offset,int count)=>inner.Write(buffer,offset,count);
        protected override void Dispose(bool disposing){if(disposing){try{inner.Dispose();}finally{Interlocked.Exchange(ref release,null)?.Invoke();}}base.Dispose(disposing);}
        public override async ValueTask DisposeAsync(){try{await inner.DisposeAsync();}finally{Interlocked.Exchange(ref release,null)?.Invoke();}GC.SuppressFinalize(this);}
    }
    private sealed class LeasedContent:HttpContent
    {
        private readonly HttpContent content;private Action? release;
        internal LeasedContent(HttpContent original,Action leave){content=original;release=leave;foreach(var h in original.Headers)Headers.TryAddWithoutValidation(h.Key,h.Value);}
        private void ReleaseOnce()=>Interlocked.Exchange(ref release,null)?.Invoke();
        protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context)
        {try{await content.CopyToAsync(stream);}finally{ReleaseOnce();}}
        protected override async Task SerializeToStreamAsync(Stream stream,TransportContext? context,CancellationToken token)
        {try{await content.CopyToAsync(stream,context,token);}finally{ReleaseOnce();}}
        protected override async Task<Stream> CreateContentReadStreamAsync()=>new LeasedStream(await content.ReadAsStreamAsync(),ReleaseOnce);
        protected override async Task<Stream> CreateContentReadStreamAsync(CancellationToken token)=>new LeasedStream(await content.ReadAsStreamAsync(token),ReleaseOnce);
        protected override bool TryComputeLength(out long length){length=content.Headers.ContentLength??0;return content.Headers.ContentLength.HasValue;}
        protected override void Dispose(bool disposing){if(disposing){try{content.Dispose();}finally{ReleaseOnce();}}base.Dispose(disposing);}
    }
}
