// SPDX-License-Identifier: MIT
using System.Reflection;
using System.Runtime.InteropServices;

namespace XG.Core;

/// <summary>只读查询 Modrinth App 的新旧实例表，使用操作系统提供的 SQLite。</summary>
internal static class ModrinthInstanceCatalog
{
    internal sealed record Entry(string Path, string? Name, string? Version, string? Loader, string? Build);

    internal static IReadOnlyList<Entry> Read(string database, CancellationToken token)
    {
        var libraryName = OperatingSystem.IsWindows() ? "winsqlite3.dll" :
            OperatingSystem.IsMacOS() ? "libsqlite3.dylib" : "libsqlite3.so.0";
        var search = OperatingSystem.IsWindows() ? DllImportSearchPath.System32 : DllImportSearchPath.SafeDirectories;
        if (!NativeLibrary.TryLoad(libraryName, Assembly.GetExecutingAssembly(), search, out var library))
            throw new IOException("系统 SQLite 不可用，无法读取 Modrinth App 实例目录。");
        try
        {
            var open = Function<Open>("sqlite3_open_v2");
            var close = Function<Release>("sqlite3_close");
            var prepare = Function<Prepare>("sqlite3_prepare_v2");
            var finalize = Function<Release>("sqlite3_finalize");
            var step = Function<Release>("sqlite3_step");
            var text = Function<Column>("sqlite3_column_text");
            var message = Function<ErrorMessage>("sqlite3_errmsg");
            var db = IntPtr.Zero;
            try
            {
                // READONLY | NOFOLLOW; the preflight also rejects DB and WAL links on
                // Windows 10 builds whose system SQLite predates SQLITE_OPEN_NOFOLLOW.
                if (open(database, out db, 0x01000001, IntPtr.Zero) != 0)
                    throw new IOException("无法打开 Modrinth App 数据库。" + Error());
                const string modern = """
                    SELECT i.path,i.name,c.game_version,c.loader,c.loader_version
                    FROM instances i JOIN instance_content_sets c
                    ON i.applied_content_set_id=c.id AND i.id=c.instance_id
                    LIMIT 512
                    """;
                const string legacy = """
                    SELECT path,name,game_version,mod_loader,mod_loader_version
                    FROM profiles LIMIT 512
                    """;
                if (prepare(db, modern, -1, out var statement, IntPtr.Zero) != 0 &&
                    prepare(db, legacy, -1, out statement, IntPtr.Zero) != 0)
                    throw new InvalidDataException("Modrinth App 数据库中找不到可识别的实例表。" + Error());
                try
                {
                    var entries = new List<Entry>();
                    int result;
                    while ((result = step(statement)) == 100)
                    {
                        token.ThrowIfCancellationRequested();
                        if (Value(0) is { } path) entries.Add(new(path, Value(1), Value(2), Value(3), Value(4)));
                    }
                    if (result != 101) throw new IOException("无法读取 Modrinth App 实例记录。" + Error());
                    return entries;
                    string? Value(int i) => Marshal.PtrToStringUTF8(text(statement, i));
                }
                finally { _ = finalize(statement); }
            }
            finally { if (db != IntPtr.Zero) _ = close(db); }
            string? Error() => db == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(message(db));
        }
        finally { NativeLibrary.Free(library); }

        T Function<T>(string name) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr db, int flags, IntPtr vfs);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Prepare(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Release(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr Column(IntPtr statement, int index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr ErrorMessage(IntPtr db);
}
