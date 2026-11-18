// SPDX-License-Identifier: MIT
namespace XG.Core;
public static class VerifiedResourceDownload {
 public static Task StageAsync(HttpClient http,Uri source,string partial,long bytes,string trustedHash,CancellationToken token=default) =>
  VerifiedTransfer.StageAsync(http,source,partial,bytes,trustedHash,(_,_)=>{},_=>{},token);
}
