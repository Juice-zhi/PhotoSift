// PhotoSift helper: lets PhotoSift.html read a phone or camera directly over USB
// (Windows Portable Devices = MTP / PTP) and import files to the PC with their capture dates.
// The device is opened read-only. Listens on localhost only and needs the per-run token.
// Written for the C# 5 compiler that ships with Windows (.NET Framework 4.x).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using ComIStream = System.Runtime.InteropServices.ComTypes.IStream;

namespace PhotoSift
{
    // ========================================================================
    // WPD COM interop: only the vtable slots we call (order matters).
    // ========================================================================
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct PropKey
    {
        public Guid fmtid;
        public uint pid;
        public PropKey(string g, uint p) { fmtid = new Guid(g); pid = p; }
    }

    [ComImport, Guid("a1567595-4c2f-4574-a6fa-ecef917b9a40"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceManager
    {
        [PreserveSig] int GetDevices([In, Out, MarshalAs(UnmanagedType.LPArray)] IntPtr[] ids, ref uint count);
        void RefreshDeviceList();
        [PreserveSig] int GetDeviceFriendlyName([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr buf, ref uint cch);
        [PreserveSig] int GetDeviceDescription([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr buf, ref uint cch);
        [PreserveSig] int GetDeviceManufacturer([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr buf, ref uint cch);
    }
    [ComImport, Guid("0af10cec-2ecd-4b92-9581-34f6ae0637f3")] class PortableDeviceManagerClass { }

    [ComImport, Guid("625e2df8-6392-4cf0-9ad1-3cfa5f17775c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDevice
    {
        void Open([MarshalAs(UnmanagedType.LPWStr)] string id, IPortableDeviceValues clientInfo);
        void SendCommand(uint flags, IntPtr parameters, out IntPtr results);
        void Content(out IPortableDeviceContent content);
        void Capabilities(out IntPtr caps);
        void Cancel();
        void Close();
    }
    [ComImport, Guid("f7c0039a-4762-488a-b4b3-760ef9a1ba9b")] class PortableDeviceFTMClass { }
    [ComImport, Guid("728a21c5-3d9e-48d7-9810-864848f0f404")] class PortableDeviceClass { }

    [ComImport, Guid("6a96ed84-7c73-4480-9938-bf5af477d426"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceContent
    {
        void EnumObjects(uint flags, [MarshalAs(UnmanagedType.LPWStr)] string parent, IPortableDeviceValues filter, out IEnumPortableDeviceObjectIDs ids);
        void Properties(out IPortableDeviceProperties props);
        void Transfer(out IPortableDeviceResources res);
    }

    [ComImport, Guid("10ece955-cf41-4728-bfa0-41eedf1bbf19"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumPortableDeviceObjectIDs
    {
        [PreserveSig] int Next(uint count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] ids, out uint fetched);
    }

    [ComImport, Guid("7f6d695c-03df-4439-a809-59266beee3a6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceProperties
    {
        void GetSupportedProperties([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr keys);
        void GetPropertyAttributes([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropKey key, out IntPtr attrs);
        void GetValues([MarshalAs(UnmanagedType.LPWStr)] string id, IPortableDeviceKeyCollection keys, out IPortableDeviceValues values);
    }

    [ComImport, Guid("fd8878ac-d841-4d17-891c-e6829cdb6934"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceResources
    {
        void GetSupportedResources([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr keys);
        void GetResourceAttributes([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropKey key, out IntPtr attrs);
        void GetStream([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropKey key, uint mode, ref uint optimalBufferSize, out ComIStream stream);
    }

    [ComImport, Guid("dada2357-e0ad-492e-98db-dd61c53ba353"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceKeyCollection
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropKey key);
        void Add(ref PropKey key);
    }
    [ComImport, Guid("de2d022d-2480-43be-97f0-d1fa2cf98f4f")] class PortableDeviceKeyCollectionClass { }

    [ComImport, Guid("6848f6f2-3155-4f86-b6f5-263eeeab3143"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPortableDeviceValues
    {
        void GetCount(out uint count);
        void GetAt(uint index, IntPtr key, IntPtr value);
        void SetValue(ref PropKey key, IntPtr value);
        void GetValue(ref PropKey key, IntPtr value);
        void SetStringValue(ref PropKey key, [MarshalAs(UnmanagedType.LPWStr)] string value);
        void GetStringValue(ref PropKey key, [MarshalAs(UnmanagedType.LPWStr)] out string value);
        void SetUnsignedIntegerValue(ref PropKey key, uint value);
        void GetUnsignedIntegerValue(ref PropKey key, out uint value);
        void SetSignedIntegerValue(ref PropKey key, int value);
        void GetSignedIntegerValue(ref PropKey key, out int value);
        void SetUnsignedLargeIntegerValue(ref PropKey key, ulong value);
        void GetUnsignedLargeIntegerValue(ref PropKey key, out ulong value);
        void SetSignedLargeIntegerValue(ref PropKey key, long value);
        void GetSignedLargeIntegerValue(ref PropKey key, out long value);
        void SetFloatValue(ref PropKey key, float value);
        void GetFloatValue(ref PropKey key, out float value);
        void SetErrorValue(ref PropKey key, int value);
        void GetErrorValue(ref PropKey key, out int value);
        void SetKeyValue(ref PropKey key, ref PropKey value);
        void GetKeyValue(ref PropKey key, out PropKey value);
        void SetBoolValue(ref PropKey key, int value);
        void GetBoolValue(ref PropKey key, out int value);
        void SetIUnknownValue(ref PropKey key, IntPtr value);
        void GetIUnknownValue(ref PropKey key, out IntPtr value);
        void SetGuidValue(ref PropKey key, ref Guid value);
        void GetGuidValue(ref PropKey key, out Guid value);
    }
    [ComImport, Guid("0c15d503-d017-47ce-9016-7b3f978721cc")] class PortableDeviceValuesClass { }

    // ========================================================================
    // Device backends
    // ========================================================================
    public class ObjInfo
    {
        public string Id, Name;
        public bool Folder;
        public long Size, Created, Modified;   // dates: wall-clock ms ("naive local", like PhotoSift.html)
    }

    // The phone handed over the previously requested file instead of the one asked for (see WpdBackend).
    public class OutOfStepException : Exception
    {
        public OutOfStepException() : base("手机传输错位：收到的是上一个文件的内容。请拔掉数据线、解锁手机后重新插上。") { }
    }

    public interface IBackend
    {
        string Root { get; }
        bool CanSeek { get; }                                      // false: reaching an offset means reading up to it
        List<string> Children(string id);
        ObjInfo Info(string id);
        int Read(string id, long offset, byte[] buf, int count);   // 0 = end of file
        byte[] Thumb(string id);                                   // null when the device has none
        void Finish();                                             // done with the file just read: complete its transfer now
        void Close();
    }

    public class WpdBackend : IBackend
    {
        const string OBJ = "ef6b490d-5cd8-437a-affc-da8b60ee4a3c", CLIENT = "204d9f0c-2292-4080-9f42-40664e70f859";
        static readonly PropKey K_NAME = new PropKey(OBJ, 4), K_FORMAT = new PropKey(OBJ, 6), K_TYPE = new PropKey(OBJ, 7),
            K_SIZE = new PropKey(OBJ, 11), K_ORIG = new PropKey(OBJ, 12), K_CREATED = new PropKey(OBJ, 18), K_MODIFIED = new PropKey(OBJ, 19);
        static readonly PropKey R_DEFAULT = new PropKey("e81e79be-34f0-41bf-b53f-f1a06ae87842", 0);
        static readonly PropKey R_THUMB = new PropKey("c7c407ba-98fa-46b5-9960-23fec124cfde", 8);
        static readonly Guid T_FOLDER = new Guid("27e2e392-a111-48e0-ab0c-e17705a05f85"),
            T_FUNCTIONAL = new Guid("99ed0160-17ff-4c44-9d98-1d7a6f941921"),
            F_ASSOCIATION = new Guid("30010000-ae6c-4804-98ba-c57b46965fe7");
        static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        // Phones (MTP/PTP) serve one file transfer at a time. On an iPhone a second open stream fails with
        // "busy" and IStream::Seek is E_NOTIMPL. Worse, a transfer cut short (stream released early, or
        // IPortableDevice::Cancel) can knock the phone out of step: later streams then deliver the previous
        // file's bytes. So on such a phone every stream is read to its end, and Dev never starts a partial
        // read of a file too big to finish (STREAM_MAX). Errors from a phone that is busy or recovering are retried.
        // A phone that is out of step anyway (another program cut a transfer short) is caught by Check().
        class OpenStream { public string Id; public ComIStream S; public long Pos, Size, PrevSize; }
        public const long STREAM_MAX = 64L << 20;
        const long DRAIN_MAX = 8L << 20, SKIP_CHEAP = 1L << 20;

        readonly IPortableDevice dev;
        readonly IPortableDeviceProperties props;
        readonly IPortableDeviceResources res;
        readonly IPortableDeviceContent content;
        readonly IPortableDeviceKeyCollection keys;
        readonly IntPtr pRead = Marshal.AllocHGlobal(8);
        readonly byte[] tmp = new byte[1 << 20];
        OpenStream cur;
        long lastSize = -1;   // listed size of the file requested most recently
        bool? canSeek;

        public string Root { get { return "DEVICE"; } }
        public bool CanSeek { get { return canSeek != false; } }

        public WpdBackend(string pnpId)
        {
            var info = (IPortableDeviceValues)new PortableDeviceValuesClass();
            PropKey k = new PropKey(CLIENT, 2); info.SetStringValue(ref k, "PhotoSift");
            k = new PropKey(CLIENT, 3); info.SetUnsignedIntegerValue(ref k, 1);
            k = new PropKey(CLIENT, 4); info.SetUnsignedIntegerValue(ref k, 0);
            k = new PropKey(CLIENT, 5); info.SetUnsignedIntegerValue(ref k, 0);
            k = new PropKey(CLIENT, 8); info.SetUnsignedIntegerValue(ref k, 0x20000);       // SECURITY_IMPERSONATION
            k = new PropKey(CLIENT, 9); info.SetUnsignedIntegerValue(ref k, 0x80000000);    // GENERIC_READ only: never write to the phone
            object d;
            try { d = new PortableDeviceFTMClass(); } catch (COMException) { d = new PortableDeviceClass(); }
            dev = (IPortableDevice)d;
            if (pnpId.IndexOf("vid_05ac", StringComparison.OrdinalIgnoreCase) >= 0) canSeek = false;   // Apple: don't even try
            dev.Open(pnpId, info);
            dev.Content(out content);
            content.Properties(out props);
            content.Transfer(out res);
            keys = (IPortableDeviceKeyCollection)new PortableDeviceKeyCollectionClass();
            foreach (PropKey key in new[] { K_NAME, K_FORMAT, K_TYPE, K_SIZE, K_ORIG, K_CREATED, K_MODIFIED }) { PropKey kk = key; keys.Add(ref kk); }
        }

        public List<string> Children(string id)
        {
            IEnumPortableDeviceObjectIDs e;
            content.EnumObjects(0, id, null, out e);
            var list = new List<string>();
            var ids = new IntPtr[100];
            try
            {
                while (true)
                {
                    uint got;
                    int hr = e.Next((uint)ids.Length, ids, out got);
                    for (int i = 0; i < got; i++) { list.Add(Marshal.PtrToStringUni(ids[i])); Marshal.FreeCoTaskMem(ids[i]); }
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    if (hr != 0 || got == 0) break;
                }
            }
            finally { Marshal.ReleaseComObject(e); }
            return list;
        }

        public ObjInfo Info(string id)
        {
            IPortableDeviceValues v;
            props.GetValues(id, keys, out v);
            try
            {
                var o = new ObjInfo { Id = id };
                o.Name = Str(v, K_ORIG) ?? Str(v, K_NAME) ?? id;
                Guid t = Gd(v, K_TYPE);
                o.Folder = t == T_FOLDER || t == T_FUNCTIONAL || (t == Guid.Empty && Gd(v, K_FORMAT) == F_ASSOCIATION);
                o.Size = (long)U64(v, K_SIZE);
                o.Created = Date(v, K_CREATED);
                o.Modified = Date(v, K_MODIFIED);
                return o;
            }
            finally { Marshal.ReleaseComObject(v); }
        }

        static string Str(IPortableDeviceValues v, PropKey k)
        {
            try { string s; v.GetStringValue(ref k, out s); return string.IsNullOrEmpty(s) ? null : s; } catch { return null; }
        }
        static Guid Gd(IPortableDeviceValues v, PropKey k)
        {
            try { Guid g; v.GetGuidValue(ref k, out g); return g; } catch { return Guid.Empty; }
        }
        static ulong U64(IPortableDeviceValues v, PropKey k)
        {
            try { ulong x; v.GetUnsignedLargeIntegerValue(ref k, out x); return x; }
            catch { try { uint y; v.GetUnsignedIntegerValue(ref k, out y); return y; } catch { return 0; } }
        }

        [DllImport("ole32.dll")] static extern int PropVariantClear(IntPtr pvar);
        static long Date(IPortableDeviceValues v, PropKey k)
        {
            IntPtr pv = Marshal.AllocCoTaskMem(32);
            try
            {
                for (int i = 0; i < 32; i += 8) Marshal.WriteInt64(pv, i, 0);
                try { v.GetValue(ref k, pv); } catch { return 0; }
                short vt = Marshal.ReadInt16(pv);
                double ms = 0;
                if (vt == 7) ms = (BitConverter.Int64BitsToDouble(Marshal.ReadInt64(pv, 8)) - 25569) * 86400000.0;   // VT_DATE: device wall clock
                else if (vt == 64) ms = (DateTime.FromFileTime(Marshal.ReadInt64(pv, 8)) - Epoch).TotalMilliseconds;  // VT_FILETIME (UTC) -> local
                else if (vt == 31) ms = ParseDate(Marshal.PtrToStringUni(Marshal.ReadIntPtr(pv, 8)));
                PropVariantClear(pv);
                return ms > 0 ? (long)Math.Round(ms) : 0;
            }
            finally { Marshal.FreeCoTaskMem(pv); }
        }
        static double ParseDate(string s)
        {
            if (s == null) return 0;
            var d = new StringBuilder();
            foreach (char c in s) if (c >= '0' && c <= '9') d.Append(c);
            if (d.Length < 14) return 0;
            DateTime t;
            return DateTime.TryParseExact(d.ToString(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? (t - Epoch).TotalMilliseconds : 0;
        }

        public int Read(string id, long offset, byte[] buf, int count)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return ReadOnce(id, offset, buf, count); }
                catch (COMException e)
                {
                    if (!Server.IsTransient(e) || attempt >= 4) throw;
                    Abandon();
                    Thread.Sleep(attempt == 0 ? 20 : 100 << attempt);
                }
            }
        }
        int ReadOnce(string id, long offset, byte[] buf, int count)
        {
            OpenStream o = StreamAt(id, offset);
            int total = 0;
            while (total < count)
            {
                o.S.Read(tmp, Math.Min(count - total, tmp.Length), pRead);
                int n = Marshal.ReadInt32(pRead);
                if (n <= 0) { Check(o); break; }
                Buffer.BlockCopy(tmp, 0, buf, total, n);
                total += n;
                o.Pos += n;
            }
            return total;
        }

        // Sequential reads (copying, video playback) continue the open stream; anything else ends it first.
        OpenStream StreamAt(string id, long offset)
        {
            if (cur != null && cur.Id == id && cur.Pos != offset)
            {
                if (offset > cur.Pos && offset - cur.Pos <= SKIP_CHEAP) Skip(cur, offset);
                else if (canSeek != false && TrySeek(cur, offset)) { }
                else if (offset > cur.Pos) Skip(cur, offset);
                else End();   // backwards without seeking: start over
            }
            if (cur != null && cur.Id != id) End();
            if (cur == null)
            {
                uint opt = 0;
                ComIStream s;
                PropKey key = R_DEFAULT;
                long size = Info(id).Size, prev = lastSize;
                lastSize = size;   // the phone counts the request even if it then fails
                res.GetStream(id, ref key, 0, ref opt, out s);
                cur = new OpenStream { Id = id, S = s, Size = size, PrevSize = prev };
                if (offset > 0 && (offset <= SKIP_CHEAP || canSeek == false || !TrySeek(cur, offset))) Skip(cur, offset);
            }
            return cur;
        }
        bool TrySeek(OpenStream o, long offset)
        {
            try { o.S.Seek(offset, 0, IntPtr.Zero); o.Pos = offset; canSeek = true; return true; }
            catch { canSeek = false; return false; }
        }
        // Devices that cannot seek: read forward and throw the bytes away (only for short distances).
        void Skip(OpenStream o, long offset)
        {
            if (offset - o.Pos > STREAM_MAX) throw new NotSupportedException("noseek");
            while (o.Pos < offset)
            {
                o.S.Read(tmp, (int)Math.Min(tmp.Length, offset - o.Pos), pRead);
                int n = Marshal.ReadInt32(pRead);
                if (n <= 0) { Check(o); break; }
                o.Pos += n;
            }
        }
        // At the end of a stream: a phone out of step hands each stream the file requested before it, so a
        // transfer that ends at exactly the previous file's size instead of its own is not this file.
        void Check(OpenStream o)
        {
            if (o.Pos != o.Size && o.PrevSize > 0 && o.Pos == o.PrevSize) throw new OutOfStepException();
        }
        // Close the open stream. On a phone that cannot seek, read it to the very end first (until Read returns
        // nothing, not just to the listed size) so the transfer completes; elsewhere only short leftovers are read.
        void End()
        {
            OpenStream o = cur;
            if (o == null) return;
            try
            {
                long left = o.Size - o.Pos;
                if (canSeek == false || left <= DRAIN_MAX)
                {
                    long limit = o.Pos + Math.Max(left, 0) + DRAIN_MAX;
                    while (o.Pos < limit)
                    {
                        o.S.Read(tmp, tmp.Length, pRead);
                        int n = Marshal.ReadInt32(pRead);
                        if (n <= 0) { Check(o); break; }
                        o.Pos += n;
                    }
                }
            }
            catch (COMException) { }
            finally { Abandon(); }
        }
        public void Finish() { End(); }
        void Abandon()
        {
            if (cur == null) return;
            try { Marshal.ReleaseComObject(cur.S); } catch { }
            cur = null;
        }

        public byte[] Thumb(string id)
        {
            uint opt = 0;
            ComIStream s;
            PropKey key = R_THUMB;
            try { res.GetStream(id, ref key, 0, ref opt, out s); } catch { return null; }
            try
            {
                var ms = new MemoryStream();
                while (ms.Length < (8 << 20))
                {
                    s.Read(tmp, tmp.Length, pRead);
                    int n = Marshal.ReadInt32(pRead);
                    if (n <= 0) break;
                    ms.Write(tmp, 0, n);
                }
                return ms.Length > 0 ? ms.ToArray() : null;
            }
            catch { return null; }
            finally { Marshal.ReleaseComObject(s); }
        }

        public void Close()
        {
            try { End(); } catch { }
            try { dev.Close(); } catch { }
            Marshal.ReleaseComObject(dev);
        }
    }

    // Test double: serves a local folder as if it were a phone (PhotoSift.exe --mock <folder>).
    // A sibling "<folder>_thumbs" may hold small JPEGs that stand in for the device's own thumbnails.
    public class FolderBackend : IBackend
    {
        readonly string root;
        static readonly DateTime Epoch = new DateTime(1970, 1, 1);
        public FolderBackend(string dir) { root = Path.GetFullPath(dir).TrimEnd('\\'); }
        public string Root { get { return ""; } }
        public bool CanSeek { get { return true; } }
        string Full(string id) { return id.Length == 0 ? root : Path.Combine(root, id); }
        public List<string> Children(string id)
        {
            var list = new List<string>();
            foreach (var e in new DirectoryInfo(Full(id)).GetFileSystemInfos()) list.Add(id.Length == 0 ? e.Name : id + "\\" + e.Name);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
        public ObjInfo Info(string id)
        {
            string p = Full(id);
            bool dir = Directory.Exists(p);
            var fi = new FileInfo(p);
            long t = (long)(fi.LastWriteTime - Epoch).TotalMilliseconds;
            return new ObjInfo { Id = id, Name = Path.GetFileName(p), Folder = dir, Size = dir ? 0 : fi.Length, Created = t, Modified = t };
        }
        public int Read(string id, long offset, byte[] buf, int count)
        {
            using (var fs = new FileStream(Full(id), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Position = offset;
                int total = 0;
                while (total < count) { int n = fs.Read(buf, total, count - total); if (n <= 0) break; total += n; }
                return total;
            }
        }
        public byte[] Thumb(string id)
        {
            string t = Path.Combine(root + "_thumbs", id + ".jpg");
            return File.Exists(t) ? File.ReadAllBytes(t) : null;
        }
        public void Finish() { }
        public void Close() { }
    }

    // One open device: serializes access (MTP handles one transaction at a time) and caches small reads,
    // because the page reads many tiny box headers while parsing metadata.
    public class Dev
    {
        public readonly string Id;
        public readonly IBackend B;
        public readonly object Sync = new object();
        readonly Dictionary<string, ObjInfo> known = new Dictionary<string, ObjInfo>();

        class Chunk { public string Id; public long Start; public byte[] Data; public long Used; }
        readonly List<Chunk> cache = new List<Chunk>();
        long cacheBytes, tick;
        const int SMALL = 256 * 1024, CHUNK = 64 * 1024, WHOLE_MAX = 8 << 20;
        const long CACHE_MAX = 48L << 20;

        public Dev(string id, IBackend b) { Id = id; B = b; }

        public List<string> Children(string id) { lock (Sync) return B.Children(id); }

        public ObjInfo Info(string id)
        {
            lock (Sync)
            {
                ObjInfo o;
                if (!known.TryGetValue(id, out o)) { o = B.Info(id); known[id] = o; }
                return o;
            }
        }

        public int Read(string id, long offset, byte[] buf, int count) { lock (Sync) return B.Read(id, offset, buf, count); }

        public byte[] Thumb(string id) { lock (Sync) return B.Thumb(id); }

        // Writes bytes [start, start+count) of an object to sink. The device lock is held only while reading,
        // never while writing to a (possibly slow) browser connection.
        public void Pipe(string id, long start, long count, Stream sink)
        {
            if (count <= 0) return;
            // A partial read of a big file on a phone that cannot seek would have to be cut short (see WpdBackend).
            if (!B.CanSeek && Info(id).Size > WpdBackend.STREAM_MAX) throw new NotSupportedException("toobig");
            if (count <= SMALL || Whole(id))
            {
                byte[] data = Small(id, start, (int)count);
                sink.Write(data, 0, data.Length);
                return;
            }
            var buf = new byte[SMALL];
            long done = 0;
            while (done < count)
            {
                int n = Read(id, start + done, buf, (int)Math.Min(buf.Length, count - done));
                if (n <= 0) break;
                sink.Write(buf, 0, n);
                done += n;
            }
        }

        byte[] Small(string id, long start, int count)
        {
            lock (Sync)
            {
                foreach (var c in cache)
                {
                    if (c.Id != id || start < c.Start || start + count > c.Start + c.Data.Length) continue;
                    c.Used = ++tick;
                    return Slice(c.Data, (int)(start - c.Start), count);
                }
                long size = Info(id).Size;
                bool whole = Whole(id);
                long at = whole ? 0 : start;
                int len = whole ? (int)size : size > start ? (int)Math.Max(count, Math.Min(CHUNK, size - start)) : count;
                var buf = new byte[len];
                int n = B.Read(id, at, buf, len);
                if (whole) B.Finish();   // checks the transfer before anything is cached
                if (n < len) Array.Resize(ref buf, n);
                cache.Add(new Chunk { Id = id, Start = at, Data = buf, Used = ++tick });
                cacheBytes += n;
                while (cacheBytes > CACHE_MAX && cache.Count > 1)
                {
                    Chunk old = cache[0];
                    foreach (var c in cache) if (c.Used < old.Used) old = c;
                    cache.Remove(old);
                    cacheBytes -= old.Data.Length;
                }
                int from = (int)Math.Min(start - at, n);
                return Slice(buf, from, Math.Min(count, n - from));
            }
        }
        // On a phone that cannot seek, every partial read costs a whole transfer anyway, so small files
        // (photos, Live Photo clips) are read once in full and served from memory.
        bool Whole(string id)
        {
            if (B.CanSeek) return false;
            long size = Info(id).Size;
            return size > 0 && size <= WHOLE_MAX;
        }
        static byte[] Slice(byte[] a, int off, int n)
        {
            var r = new byte[n];
            Buffer.BlockCopy(a, off, r, 0, n);
            return r;
        }
    }

    public static class Devices
    {
        public const string MockId = "mock";
        public static string MockDir;
        static readonly Dictionary<string, Dev> open = new Dictionary<string, Dev>();

        public static List<object> List(bool all)
        {
            var list = new List<object>();
            if (MockDir != null) list.Add(Row(MockId, "Mock iPhone", "Apple Inc.", "Test folder " + MockDir, "phone"));
            var mgr = (IPortableDeviceManager)new PortableDeviceManagerClass();
            try
            {
                mgr.RefreshDeviceList();
                uint n = 0;
                mgr.GetDevices(null, ref n);
                if (n == 0) return list;
                var ids = new IntPtr[n];
                Marshal.ThrowExceptionForHR(mgr.GetDevices(ids, ref n));
                for (int i = 0; i < n; i++)
                {
                    string id = Marshal.PtrToStringUni(ids[i]);
                    Marshal.FreeCoTaskMem(ids[i]);
                    // Drives (USB sticks, card readers) also show up in WPD; they already have a drive letter.
                    bool drive = id.ToLowerInvariant().Contains("wpdbusenum");
                    if (drive && !all) continue;
                    list.Add(Row(id, DevStr(mgr, id, 0), DevStr(mgr, id, 1), DevStr(mgr, id, 2), drive ? "drive" : "phone"));
                }
            }
            finally { Marshal.ReleaseComObject(mgr); }
            return list;
        }
        static Dictionary<string, object> Row(string id, string name, string maker, string desc, string kind)
        {
            return new Dictionary<string, object> { { "id", id }, { "name", name }, { "maker", maker }, { "desc", desc }, { "kind", kind } };
        }
        static string DevStr(IPortableDeviceManager m, string id, int which)
        {
            uint cch = 0;
            Call(m, id, which, IntPtr.Zero, ref cch);
            if (cch == 0) return "";
            IntPtr b = Marshal.AllocHGlobal((int)(cch + 1) * 2);
            try { return Call(m, id, which, b, ref cch) < 0 ? "" : (Marshal.PtrToStringUni(b) ?? "").Trim(); }
            finally { Marshal.FreeHGlobal(b); }
        }
        static int Call(IPortableDeviceManager m, string id, int which, IntPtr b, ref uint cch)
        {
            return which == 0 ? m.GetDeviceFriendlyName(id, b, ref cch) : which == 1 ? m.GetDeviceManufacturer(id, b, ref cch) : m.GetDeviceDescription(id, b, ref cch);
        }

        public static Dev Get(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("dev");
            lock (open)
            {
                Dev d;
                if (open.TryGetValue(id, out d)) return d;
                IBackend b = id == MockId && MockDir != null ? (IBackend)new FolderBackend(MockDir) : new WpdBackend(id);
                d = new Dev(id, b);
                open[id] = d;
                return d;
            }
        }
        public static void Drop(string id)
        {
            Dev d;
            lock (open)
            {
                if (!open.TryGetValue(id, out d)) return;
                open.Remove(id);
            }
            try { lock (d.Sync) d.B.Close(); } catch { }
        }
    }

    // ========================================================================
    // Import job: copies device files into a PC folder, then stamps them with the capture time.
    // ========================================================================
    public class ImportJob
    {
        public class Item { public string Id, Rel; public long Size, Ts; }
        static readonly DateTime Epoch = new DateTime(1970, 1, 1);

        public string Id, Dest, State = "running", Error, Current;
        public volatile bool Cancel;
        public int Files, Done, Skipped, Failed;
        public long Bytes, Total;
        public readonly List<int> Ok = new List<int>();
        public readonly List<string> Errors = new List<string>();

        public Dictionary<string, object> Snapshot()
        {
            lock (this)
            {
                var d = new Dictionary<string, object> {
                    { "state", State }, { "error", Error }, { "current", Current }, { "files", Files }, { "done", Done },
                    { "skipped", Skipped }, { "failed", Failed }, { "bytes", Bytes }, { "total", Total }, { "errors", Errors.ToArray() } };
                if (State != "running") d["ok"] = Ok.ToArray();
                return d;
            }
        }

        public void Run(Dev dev, List<Item> items)
        {
            var written = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var buf = new byte[1 << 20];
            for (int i = 0; i < items.Count && !Cancel; i++)
            {
                Item it = items[i];
                lock (this) Current = it.Rel;
                string part = null;
                long got = 0;
                try
                {
                    string target = SafeJoin(Dest, it.Rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    if (File.Exists(target) && !written.ContainsKey(target) && new FileInfo(target).Length == it.Size)
                    {
                        lock (this) { Skipped++; Done++; Bytes += it.Size; Ok.Add(i); }
                        continue;
                    }
                    string final = FreeName(target);
                    part = final + ".part";
                    // Hold the phone for the whole file: reading another file in between would cut this transfer short.
                    lock (dev.Sync)
                    {
                        for (int attempt = 1; ; attempt++)
                        {
                            Exception err = null;
                            got = 0;
                            try { CopyTo(dev, it.Id, part, buf, ref got); }
                            catch (Exception e) { err = e; }
                            if (err == null && got == it.Size) break;
                            if (Cancel) break;
                            if (err != null && (Server.IsGone(err) || err is OutOfStepException)) throw err;
                            lock (this) Bytes -= got;
                            if (attempt >= 3)
                            {
                                if (err != null) { got = 0; throw err; }
                                string msg = "大小不符：收到 " + got + " 字节，手机上是 " + it.Size + " 字节。iPhone 请在 设置 → 照片 → 传输到 Mac 或 PC 选「保留原片」。";
                                got = 0;
                                throw new IOException(msg);
                            }
                        }
                    }
                    if (got != it.Size) break;   // stopped mid-file
                    File.Move(part, final);
                    part = null;
                    if (it.Ts > 0)
                    {
                        DateTime t = Epoch.AddMilliseconds(it.Ts);   // wall clock, applied as local time
                        File.SetCreationTime(final, t);
                        File.SetLastWriteTime(final, t);
                    }
                    written[final] = true;
                    lock (this) { Done++; Ok.Add(i); }
                }
                catch (Exception e)
                {
                    lock (this)
                    {
                        Failed++; Done++; Bytes += Math.Max(0, it.Size - got);
                        if (Errors.Count < 50) Errors.Add(it.Rel + ": " + e.Message);
                    }
                    if (Server.IsGone(e)) { Devices.Drop(dev.Id); lock (this) Error = "gone"; break; }
                    if (e is OutOfStepException) { Devices.Drop(dev.Id); lock (this) Error = "outofstep"; break; }
                }
                finally
                {
                    if (part != null) try { File.Delete(part); } catch { }
                }
            }
            lock (this) { State = Cancel ? "cancelled" : Error != null ? "error" : "done"; Current = null; }
        }

        // Reads the file to the end. A phone that cannot seek must not have a transfer cut short, so there a
        // stop request takes effect after the current file.
        void CopyTo(Dev dev, string id, string part, byte[] buf, ref long got)
        {
            bool finish = !dev.B.CanSeek;
            using (var fs = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                while (!Cancel || finish)
                {
                    int n = dev.Read(id, got, buf, buf.Length);
                    if (n <= 0) break;
                    fs.Write(buf, 0, n);
                    got += n;
                    lock (this) Bytes += n;
                }
            }
        }

        static string SafeJoin(string dest, string rel)
        {
            string p = dest;
            foreach (string seg in rel.Split('/', '\\'))
            {
                string s = seg.Trim();
                if (s.Length == 0 || s == "." || s == "..") continue;
                foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
                p = Path.Combine(p, s);
            }
            return p;
        }
        static string FreeName(string target)
        {
            if (!File.Exists(target)) return target;
            string dir = Path.GetDirectoryName(target), stem = Path.GetFileNameWithoutExtension(target), ext = Path.GetExtension(target);
            for (int i = 1; ; i++)
            {
                string p = Path.Combine(dir, stem + " (" + i + ")" + ext);
                if (!File.Exists(p)) return p;
            }
        }
    }

    // ========================================================================
    // Native folder picker (the browser cannot hand out real paths).
    // ========================================================================
    [ComImport, Guid("dc1c5a9c-e88a-4dde-a5a1-60f82a20aef7")] class FileOpenDialogClass { }

    [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr parent);
        void SetFileTypes(uint count, IntPtr specs);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint fos);
        void GetOptions(out uint fos);
        void SetDefaultFolder(IShellItem item);
        void SetFolder(IShellItem item);
        void GetFolder(out IShellItem item);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetResult(out IShellItem item);
    }

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    }

    public static class Dialogs
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, out IShellItem item);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr pid);
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();

        static readonly object busy = new object();

        public static string PickFolder(string initial)
        {
            if (!Monitor.TryEnter(busy)) throw new InvalidOperationException("busy");
            try
            {
                string result = null;
                Exception err = null;
                var th = new Thread(() => { try { result = Show(initial); } catch (Exception e) { err = e; } });
                th.SetApartmentState(ApartmentState.STA);
                th.Start();
                th.Join();
                if (err != null) throw err;
                return result;
            }
            finally { Monitor.Exit(busy); }
        }

        static string Show(string initial)
        {
            // An invisible topmost owner so the dialog comes up in front of the browser.
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            using (var owner = new Form { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 3), Size = new Size(1, 1), TopMost = true, Opacity = 0 })
            {
                owner.Show();
                IntPtr fg = GetForegroundWindow();
                uint fgThread = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
                if (fgThread != 0 && fgThread != me) AttachThreadInput(me, fgThread, true);
                SetForegroundWindow(owner.Handle);
                if (fgThread != 0 && fgThread != me) AttachThreadInput(me, fgThread, false);

                var dlg = (IFileDialog)new FileOpenDialogClass();
                try
                {
                    dlg.SetOptions(0x20 | 0x40 | 0x800 | 0x8);   // PICKFOLDERS | FORCEFILESYSTEM | PATHMUSTEXIST | NOCHANGEDIR
                    dlg.SetTitle("选择导入到哪个文件夹");   // 选择导入到哪个文件夹
                    string start = initial;
                    while (!string.IsNullOrEmpty(start) && !Directory.Exists(start)) start = Path.GetDirectoryName(start);
                    if (!string.IsNullOrEmpty(start))
                    {
                        try
                        {
                            Guid iid = typeof(IShellItem).GUID;
                            IShellItem si;
                            SHCreateItemFromParsingName(start, IntPtr.Zero, ref iid, out si);
                            dlg.SetFolder(si);
                        }
                        catch { }
                    }
                    int hr = dlg.Show(owner.Handle);
                    if (hr == unchecked((int)0x800704C7)) return null;   // cancelled
                    Marshal.ThrowExceptionForHR(hr);
                    IShellItem item;
                    dlg.GetResult(out item);
                    string path;
                    item.GetDisplayName(0x80058000, out path);   // SIGDN_FILESYSPATH
                    return path;
                }
                finally { Marshal.ReleaseComObject(dlg); }
            }
        }
    }

    // ========================================================================
    // HTTP server
    // ========================================================================
    public class Server
    {
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        static readonly Dictionary<string, ImportJob> jobs = new Dictionary<string, ImportJob>();
        static readonly Dictionary<string, string> Mime = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { ".mov", "video/quicktime" }, { ".mp4", "video/mp4" }, { ".m4v", "video/mp4" }, { ".3gp", "video/3gpp" },
            { ".heic", "image/heic" }, { ".heif", "image/heif" }, { ".jpg", "image/jpeg" }, { ".jpeg", "image/jpeg" },
            { ".png", "image/png" }, { ".gif", "image/gif" }, { ".webp", "image/webp" }, { ".avif", "image/avif" } };

        readonly string appDir, token;
        HttpListener http;
        public int Port;

        public Server(string appDir, string token) { this.appDir = appDir; this.token = token; }

        public int Start(int first)
        {
            for (int p = first; p < first + 20; p++)
            {
                var h = new HttpListener();
                h.Prefixes.Add("http://localhost:" + p + "/");
                h.Prefixes.Add("http://127.0.0.1:" + p + "/");
                try { h.Start(); http = h; Port = p; return p; }
                catch (HttpListenerException) { h.Close(); }
            }
            throw new Exception("no free port");
        }

        public void Loop()
        {
            int w, io;
            ThreadPool.GetMinThreads(out w, out io);
            ThreadPool.SetMinThreads(Math.Max(w, 32), Math.Max(io, 32));
            while (http.IsListening)
            {
                HttpListenerContext ctx;
                try { ctx = http.GetContext(); } catch { break; }
                ThreadPool.QueueUserWorkItem(Handle, ctx);
            }
        }

        void Handle(object state)
        {
            var c = (HttpListenerContext)state;
            HttpListenerRequest q = c.Request;
            HttpListenerResponse r = c.Response;
            try
            {
                string path = q.Url.AbsolutePath;
                r.AddHeader("Cache-Control", "no-store");
                if (path == "/" || path == "/index.html") { ServeHtml(r); return; }
                if (!path.StartsWith("/api/")) { Send(r, 404, Err("notfound")); return; }
                // Only our own page may call the API: same origin + the token it was served with.
                string origin = q.Headers["Origin"];
                if (origin != null && origin != "http://localhost:" + Port && origin != "http://127.0.0.1:" + Port) { Send(r, 403, Err("origin")); return; }
                if ((q.Headers["X-PS-Token"] ?? q.QueryString["t"]) != token) { Send(r, 403, Err("token")); return; }
                switch (path)
                {
                    case "/api/ping":
                        Send(r, 200, new Dictionary<string, object> { { "ok", true }, { "version", 1 }, { "mock", Devices.MockDir != null },
                            { "pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) } });
                        break;
                    case "/api/devices": Send(r, 200, Devices.List(q.QueryString["all"] == "1")); break;
                    case "/api/folders": Folders(q, r); break;
                    case "/api/scan": Scan(q, r); break;
                    case "/api/file": FileRange(q, r); break;
                    case "/api/thumb": Thumb(q, r); break;
                    case "/api/pick-folder":
                        {
                            string p = Dialogs.PickFolder(S(Body(q), "initial"));
                            Send(r, 200, new Dictionary<string, object> { { "path", p } });
                            break;
                        }
                    case "/api/import": StartImport(q, r); break;
                    case "/api/job":
                        {
                            ImportJob j;
                            lock (jobs) jobs.TryGetValue(q.QueryString["id"] ?? "", out j);
                            if (j == null) Send(r, 404, Err("nojob")); else Send(r, 200, j.Snapshot());
                            break;
                        }
                    case "/api/job-cancel":
                        {
                            ImportJob j;
                            lock (jobs) jobs.TryGetValue(S(Body(q), "job") ?? "", out j);
                            if (j != null) j.Cancel = true;
                            Send(r, 200, new Dictionary<string, object> { { "ok", j != null } });
                            break;
                        }
                    case "/api/reveal":
                        {
                            string p = S(Body(q), "path");
                            bool ok = !string.IsNullOrEmpty(p) && Directory.Exists(p);
                            if (ok) Process.Start("explorer.exe", "\"" + p + "\"");
                            Send(r, 200, new Dictionary<string, object> { { "ok", ok } });
                            break;
                        }
                    case "/api/dest-index": DestIndex(q, r); break;
                    default: Send(r, 404, Err("notfound")); break;
                }
            }
            catch (Exception e)
            {
                if (e is HttpListenerException || e is ObjectDisposedException) return;   // browser went away mid-response
                string code = IsGone(e) ? "gone" : IsDenied(e) ? "denied" : IsTransient(e) ? "busy" : e is OutOfStepException ? "outofstep" : e.Message;
                string dev = q.QueryString["dev"];
                if ((code == "gone" || code == "outofstep") && dev != null) Devices.Drop(dev);
                try { Send(r, code == "gone" ? 503 : 500, Err(code)); } catch { }
            }
            finally { try { r.Close(); } catch { } }
        }

        public static bool IsGone(Exception e)
        {
            var ce = e as COMException;
            if (ce == null) return false;
            switch ((uint)ce.ErrorCode)
            {
                case 0x8007001F: case 0x80070015: case 0x8007048F: case 0x800701B1: case 0x80070079:
                case 0x800706BA: case 0x80070006: case 0x80070037: case 0x802A0006:
                    return true;
            }
            return false;
        }
        // The phone is busy with another transfer, or still recovering from one that was cut short.
        public static bool IsTransient(Exception e)
        {
            var ce = e as COMException;
            if (ce == null) return false;
            switch ((uint)ce.ErrorCode)
            {
                case 0x800700AA:   // ERROR_BUSY
                case 0x8007001E:   // ERROR_READ_FAULT
                case 0x80042007:   // PTP Incomplete_Transfer
                case 0x80042019:   // PTP Device_Busy
                    return true;
            }
            return false;
        }
        static bool IsDenied(Exception e)
        {
            var ce = e as COMException;
            return ce != null && (uint)ce.ErrorCode == 0x80070005 || e is UnauthorizedAccessException;
        }

        void ServeHtml(HttpListenerResponse r)
        {
            string html = File.ReadAllText(Path.Combine(appDir, "PhotoSift.html"), Encoding.UTF8)
                .Replace("<meta name=\"ps-token\" content=\"\">", "<meta name=\"ps-token\" content=\"" + token + "\">");
            byte[] b = Encoding.UTF8.GetBytes(html);
            r.ContentType = "text/html; charset=utf-8";
            r.AddHeader("X-Frame-Options", "DENY");
            r.ContentLength64 = b.Length;
            r.OutputStream.Write(b, 0, b.Length);
        }

        // Storages (e.g. "Internal Storage") and their top-level folders, so the page can pick DCIM etc.
        static void Folders(HttpListenerRequest q, HttpListenerResponse r)
        {
            Dev d = Devices.Get(q.QueryString["dev"]);
            var list = new List<object>();
            foreach (string sid in d.Children(d.B.Root))
            {
                ObjInfo si = d.Info(sid);
                var folders = new List<object>();
                int files = 0;
                if (si.Folder)
                {
                    foreach (string k in d.Children(sid))
                    {
                        ObjInfo ki = d.Info(k);
                        if (Hidden(ki.Name)) continue;
                        if (ki.Folder) folders.Add(new Dictionary<string, object> { { "id", k }, { "name", ki.Name } });
                        else files++;
                    }
                }
                list.Add(new Dictionary<string, object> { { "id", sid }, { "name", si.Name }, { "folders", folders }, { "files", files } });
            }
            Send(r, 200, list);
        }

        // Streams the file list as NDJSON so the page can show progress on big libraries.
        static void Scan(HttpListenerRequest q, HttpListenerResponse r)
        {
            Dictionary<string, object> body = Body(q);
            Dev d = Devices.Get(S(body, "dev"));
            r.ContentType = "application/x-ndjson; charset=utf-8";
            r.SendChunked = true;
            var w = new StreamWriter(r.OutputStream, new UTF8Encoding(false), 1 << 16);
            var sw = Stopwatch.StartNew();
            long lastFlush = 0;
            int count = 0;
            Action<string, string> walk = null;
            walk = (id, prefix) =>
            {
                foreach (string k in d.Children(id))
                {
                    ObjInfo o = d.Info(k);
                    if (Hidden(o.Name)) continue;
                    if (o.Folder) { walk(k, prefix + o.Name + "/"); continue; }
                    count++;
                    w.Write("{\"t\":\"f\",\"id\":" + Json.Serialize(k) + ",\"p\":" + Json.Serialize(prefix + o.Name) + ",\"n\":" + Json.Serialize(o.Name)
                        + ",\"s\":" + o.Size.ToString(CultureInfo.InvariantCulture) + ",\"c\":" + o.Created.ToString(CultureInfo.InvariantCulture)
                        + ",\"m\":" + o.Modified.ToString(CultureInfo.InvariantCulture) + "}\n");
                    if (sw.ElapsedMilliseconds - lastFlush > 300)
                    {
                        lastFlush = sw.ElapsedMilliseconds;
                        w.Write("{\"t\":\"p\",\"n\":" + count + "}\n");
                        w.Flush();
                    }
                }
            };
            try
            {
                var roots = body.ContainsKey("roots") ? body["roots"] as IList : null;
                if (roots != null)
                {
                    foreach (object o in roots)
                    {
                        var root = o as Dictionary<string, object>;
                        if (root != null) walk(S(root, "id"), (S(root, "path") ?? "").Trim('/') + "/");
                    }
                }
                w.Write("{\"t\":\"end\",\"n\":" + count + "}\n");
            }
            catch (Exception e)
            {
                if (e is HttpListenerException || e is ObjectDisposedException) throw;
                if (IsGone(e)) Devices.Drop(d.Id);
                w.Write("{\"t\":\"err\",\"msg\":" + Json.Serialize(IsGone(e) ? "gone" : IsDenied(e) ? "denied" : e.Message) + "}\n");
            }
            w.Flush();
        }

        // GET /api/file?dev&id[&off&len] with HTTP Range support; off/len expose a slice as its own file
        // (the video inside an Android motion photo).
        static void FileRange(HttpListenerRequest q, HttpListenerResponse r)
        {
            Dev d = Devices.Get(q.QueryString["dev"]);
            string id = q.QueryString["id"];
            long full = d.Info(id).Size;
            long off = Math.Min(Num(q.QueryString["off"], 0), full);
            long len = Math.Min(Num(q.QueryString["len"], full - off), full - off);
            long start = 0, end = len - 1;
            bool partial = false;
            string range = q.Headers["Range"];
            if (range != null && range.StartsWith("bytes=") && !range.Contains(","))
            {
                string[] ab = range.Substring(6).Split('-');
                if (ab.Length == 2)
                {
                    if (ab[0].Length == 0) { long n = Num(ab[1], 0); start = Math.Max(0, len - n); }
                    else { start = Num(ab[0], 0); if (ab[1].Length > 0) end = Math.Min(end, Num(ab[1], end)); }
                    partial = true;
                }
                if (start >= len || end < start)
                {
                    r.StatusCode = 416;
                    r.AddHeader("Content-Range", "bytes */" + len);
                    return;
                }
            }
            string type;
            r.ContentType = Mime.TryGetValue(Path.GetExtension(q.QueryString["n"] ?? ""), out type) ? type : "application/octet-stream";
            r.AddHeader("Accept-Ranges", "bytes");
            if (partial)
            {
                r.StatusCode = 206;
                r.AddHeader("Content-Range", "bytes " + start + "-" + end + "/" + len);
            }
            r.ContentLength64 = Math.Max(0, end - start + 1);
            if (q.HttpMethod == "HEAD" || len == 0) return;
            d.Pipe(id, off + start, end - start + 1, r.OutputStream);
        }

        static void Thumb(HttpListenerRequest q, HttpListenerResponse r)
        {
            Dev d = Devices.Get(q.QueryString["dev"]);
            byte[] t = d.Thumb(q.QueryString["id"]);
            if (t == null || t.Length < 4) { r.StatusCode = 204; return; }   // none: the page decodes the file instead
            r.ContentType = t[0] == 0x89 && t[1] == 0x50 ? "image/png" : "image/jpeg";
            r.ContentLength64 = t.Length;
            r.OutputStream.Write(t, 0, t.Length);
        }

        static void StartImport(HttpListenerRequest q, HttpListenerResponse r)
        {
            Dictionary<string, object> body = Body(q);
            Dev d = Devices.Get(S(body, "dev"));
            string dest = (S(body, "dest") ?? "").Trim();
            if (dest.Length < 3 || !Path.IsPathRooted(dest)) { Send(r, 400, Err("dest")); return; }
            dest = Path.GetFullPath(dest);
            var items = new List<ImportJob.Item>();
            long total = 0;
            var list = body.ContainsKey("items") ? body["items"] as IList : null;
            if (list != null)
            {
                foreach (object o in list)
                {
                    var x = o as Dictionary<string, object>;
                    if (x == null) continue;
                    var it = new ImportJob.Item { Id = S(x, "id"), Rel = S(x, "rel") ?? "", Size = L(x, "size"), Ts = L(x, "ts") };
                    if (string.IsNullOrEmpty(it.Id) || it.Rel.Length == 0) continue;
                    items.Add(it);
                    total += it.Size;
                }
            }
            if (items.Count == 0) { Send(r, 400, Err("empty")); return; }
            try { Directory.CreateDirectory(dest); }
            catch (Exception e) { Send(r, 400, new Dictionary<string, object> { { "error", "dest" }, { "detail", e.Message } }); return; }
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(dest));
                if (drive.IsReady && drive.AvailableFreeSpace < total + (64L << 20))
                {
                    Send(r, 400, new Dictionary<string, object> { { "error", "nospace" }, { "free", drive.AvailableFreeSpace }, { "need", total } });
                    return;
                }
            }
            catch { /* network paths etc. */ }
            var job = new ImportJob { Id = Guid.NewGuid().ToString("N").Substring(0, 12), Dest = dest, Files = items.Count, Total = total };
            lock (jobs) jobs[job.Id] = job;
            var th = new Thread(() => job.Run(d, items)) { IsBackground = true, Name = "import" };
            th.Start();
            Send(r, 200, new Dictionary<string, object> { { "job", job.Id }, { "dest", dest } });
        }

        // "name|size" of every file already under the destination, so the page can mark what was imported before.
        static void DestIndex(HttpListenerRequest q, HttpListenerResponse r)
        {
            string dest = S(Body(q), "dest");
            var list = new List<string>();
            if (!string.IsNullOrEmpty(dest) && Path.IsPathRooted(dest) && Directory.Exists(dest)) Index(new DirectoryInfo(dest), list, 0);
            Send(r, 200, new Dictionary<string, object> { { "files", list } });
        }
        static void Index(DirectoryInfo di, List<string> list, int depth)
        {
            try
            {
                foreach (var f in di.EnumerateFiles())
                {
                    if (list.Count >= 1000000) return;
                    list.Add(f.Name.ToLowerInvariant() + "|" + f.Length.ToString(CultureInfo.InvariantCulture));
                }
                if (depth >= 12) return;
                foreach (var s in di.EnumerateDirectories())
                    if ((s.Attributes & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint)) == 0) Index(s, list, depth + 1);
            }
            catch { /* unreadable folder */ }
        }

        // ---- small helpers ----
        static bool Hidden(string name)
        {
            return name.StartsWith(".") || name.StartsWith("$") || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);
        }
        static Dictionary<string, object> Err(string code) { return new Dictionary<string, object> { { "error", code } }; }
        static void Send(HttpListenerResponse r, int status, object body)
        {
            byte[] b = Encoding.UTF8.GetBytes(Json.Serialize(body));
            r.StatusCode = status;
            r.ContentType = "application/json; charset=utf-8";
            r.ContentLength64 = b.Length;
            r.OutputStream.Write(b, 0, b.Length);
        }
        static Dictionary<string, object> Body(HttpListenerRequest q)
        {
            using (var sr = new StreamReader(q.InputStream, Encoding.UTF8))
                return Json.DeserializeObject(sr.ReadToEnd()) as Dictionary<string, object> ?? new Dictionary<string, object>();
        }
        static string S(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        }
        static long L(Dictionary<string, object> d, string k)
        {
            object v;
            if (!d.TryGetValue(k, out v) || v == null) return 0;
            try { return Convert.ToInt64(v, CultureInfo.InvariantCulture); } catch { return 0; }
        }
        static long Num(string s, long def)
        {
            long n;
            return s != null && long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 ? n : def;
        }
    }

    public static class Program
    {
        // PhotoSift.exe [--mock <folder>] [--no-browser] [--port N]
        [MTAThread]
        public static int Main(string[] args)
        {
            string mock = null;
            bool noBrowser = false;
            int port = 47819;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--mock" && i + 1 < args.Length) mock = Path.GetFullPath(args[++i]);
                else if (args[i] == "--no-browser") noBrowser = true;
                else if (args[i] == "--port" && i + 1 < args.Length) int.TryParse(args[++i], out port);
            }
            string appDir = Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'));
            if (!File.Exists(Path.Combine(appDir, "PhotoSift.html")))
            {
                Console.WriteLine("PhotoSift.html was not found in " + appDir);
                return 1;
            }
            string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoSift");
            Directory.CreateDirectory(cacheDir);
            string session = Path.Combine(cacheDir, "session.txt");
            if (mock == null && TryReuse(session, noBrowser)) return 0;

            Devices.MockDir = mock;
            var server = new Server(appDir, Guid.NewGuid().ToString("N"));
            string url = "http://localhost:" + server.Start(port) + "/";
            if (mock == null) File.WriteAllText(session, server.Port + " " + Process.GetCurrentProcess().Id);
            Banner(url, mock != null);
            if (!noBrowser) OpenBrowser(url);
            try { server.Loop(); }
            finally { if (mock == null) try { File.Delete(session); } catch { } }
            return 0;
        }

        // A second double-click just opens another tab on the helper that is already running.
        static bool TryReuse(string session, bool noBrowser)
        {
            try
            {
                string[] parts = File.ReadAllText(session).Trim().Split(' ');
                int port = int.Parse(parts[0]);
                Process.GetProcessById(int.Parse(parts[1]));
                string url = "http://localhost:" + port + "/";
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 3000;
                using (req.GetResponse()) { }
                if (!noBrowser) OpenBrowser(url);
                Console.WriteLine("PhotoSift is already running: " + url);
                Thread.Sleep(1500);
                return true;
            }
            catch { return false; }
        }

        static void OpenBrowser(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        static void Banner(string url, bool mock)
        {
            Console.WriteLine();
            Console.WriteLine("  PhotoSift " + (mock ? "(mock device)" : "") + "  " + url);
            Console.WriteLine();
            Console.WriteLine("  已在浏览器里打开。用数据线连接手机并解锁，然后点「连接手机」。");
            // 已在浏览器里打开。用数据线连接手机并解锁，然后点「连接手机」。
            Console.WriteLine("  使用期间请保持这个窗口打开，关掉它就退出。");
            // 使用期间请保持这个窗口打开，关掉它就退出。
            Console.WriteLine();
        }
    }
}
