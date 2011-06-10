namespace Core.FS

open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open Microsoft.Win32.SafeHandles

// file change watcher
type Watcher(path, callback) =
    // native createfile to avoid exceptions when checking for file read
    [<DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)>]
    static extern SafeFileHandle CreateFile(string lpFileName, int dwDesiredAccess, FileShare dwShareMode, nativeint securityAttrs, FileMode dwCreationDisposition, int dwFlagsAndAttributes, nativeint hTemplateFile);

    // watcher object
    let watcher = new FileSystemWatcher(path, IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite, InternalBufferSize = 65536, EnableRaisingEvents = true)

    // do all processing in a separate thread to reduce notification buffer overflows
    let processor = MailboxProcessor.Start(fun inbox ->
        let cache = Dictionary<string, _>()

        let rec loop () = async {
            let! msg = inbox.Receive()

            // skip files that can't be read (some notifications arrive while the file is being written by the other process)
            if (use handle = CreateFile(msg, (* FILE_READ_DATA *) 1, FileShare.Read, 0n, FileMode.Open, 0, 0n) in handle.IsInvalid) then
                if Marshal.GetLastWin32Error() = (* ERROR_SHARING_VIOLATION *) 32 then
                    // another process is writing the file, retry with some delay
                    let! _ =
                        async {
                            do! Async.Sleep(50)
                            inbox.Post(msg)
                        } |> Async.StartChild
                    ()
                return! loop ()

            // get normalized path & modtime
            let path = Path.GetFullPath(msg).ToLowerInvariant()
            let info = File.GetLastWriteTimeUtc(path)

            // skip duplicate notifications
            match cache.TryGetValue(path) with
            | true, mtime when mtime = info -> ()
            | _ ->
                cache.[path] <- info
                callback path

            return! loop () }

        // process file updates
        loop ())

    do watcher.Changed.Add(fun args -> processor.Post(args.FullPath))