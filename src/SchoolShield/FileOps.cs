using System;
using System.Collections.Generic;
using System.IO;

namespace SchoolShield
{
    /// <summary>
    /// Copying and moving files and folders for the teacher's file manager. Runs on a worker thread (nothing here touches the window):
    /// the window reads the counters. A name that is taken becomes "Name (2).ext", nothing is ever overwritten and no question is asked.
    /// </summary>
    internal sealed class FileJob
    {
        public readonly List<string> Sources;
        public readonly string Target;
        public readonly bool Move;

        public long TotalBytes;
        public long DoneBytes;
        public int Total;
        public int Done;
        public string Current = "";
        public volatile bool Cancelled;
        public volatile bool Finished;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Created = new List<string>();

        private const int Buffer = 1 << 20;

        public FileJob(IEnumerable<string> sources, string target, bool move)
        {
            Sources = new List<string>(sources);
            Target = target;
            Move = move;
        }

        public int Percent { get { return TotalBytes <= 0 ? (Done >= Total && Total > 0 ? 100 : 0) : (int)Math.Min(100, DoneBytes * 100 / TotalBytes); } }

        // ------------------------------------------------------------------ names

        /// <summary>"Report.docx" in a folder that has it already gives "Report (2).docx", then "(3)"...</summary>
        public static string UniquePath(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
            bool dir = Directory.Exists(path);
            if (dir) { stem = name; ext = ""; }
            for (int i = 2; i < 10000; i++)
            {
                string candidate = Path.Combine(folder, stem + " (" + i + ")" + ext);
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
            return Path.Combine(folder, stem + " (" + Guid.NewGuid().ToString("N").Substring(0, 6) + ")" + ext);
        }

        /// <summary>Is <paramref name="path"/> the folder <paramref name="parent"/> itself or something inside it?</summary>
        public static bool IsInside(string path, string parent)
        {
            try
            {
                string p = Path.GetFullPath(path).TrimEnd('\\', '/');
                string r = Path.GetFullPath(parent).TrimEnd('\\', '/');
                if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
                return p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Both paths on the same disk: a move is only a rename then (instant, whatever the size).</summary>
        public static bool SameVolume(string a, string b)
        {
            try { return string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static long SizeOf(string path)
        {
            long total = 0;
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
                if (!Directory.Exists(path)) return 0;
                foreach (string f in Directory.GetFiles(path)) { try { total += new FileInfo(f).Length; } catch { } }
                foreach (string d in Directory.GetDirectories(path)) total += SizeOf(d);
            }
            catch { }
            return total;
        }

        // ------------------------------------------------------------------ the job

        public void Run()
        {
            try
            {
                List<string> todo = new List<string>();
                foreach (string s in Sources)
                {
                    if (!File.Exists(s) && !Directory.Exists(s)) { Errors.Add("Не найдено: " + Path.GetFileName(s)); continue; }
                    if (Directory.Exists(s) && IsInside(Target, s)) { Errors.Add("Папку «" + Path.GetFileName(s) + "» нельзя поместить внутрь самой себя."); continue; }
                    todo.Add(s);
                }
                Total = todo.Count;
                foreach (string s in todo) TotalBytes += SizeOf(s);

                try { Directory.CreateDirectory(Target); }
                catch (Exception ex) { Errors.Add("Не удалось открыть папку назначения: " + ex.Message); return; }

                foreach (string s in todo)
                {
                    if (Cancelled) break;
                    Current = Path.GetFileName(s.TrimEnd('\\', '/'));
                    try
                    {
                        bool isDir = Directory.Exists(s);
                        string name = Path.GetFileName(s.TrimEnd('\\', '/'));
                        string parent = Path.GetDirectoryName(s.TrimEnd('\\', '/'));
                        if (Move && string.Equals(parent, Target.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        {
                            Done++;                  // moved into the folder it is in already
                            DoneBytes += SizeOf(s);
                            continue;
                        }
                        string dst = UniquePath(Target, name);
                        if (Move && SameVolume(s, Target))
                        {
                            long size = SizeOf(s);
                            if (isDir) Directory.Move(s, dst); else File.Move(s, dst);
                            DoneBytes += size;
                        }
                        else
                        {
                            if (isDir) CopyDirectory(s, dst); else CopyFile(s, dst);
                            if (Move && !Cancelled) DeleteSource(s, isDir);
                        }
                        Created.Add(dst);
                        Done++;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { Errors.Add(Current + ": " + ex.Message); }
                }
            }
            finally { Finished = true; }
        }

        private void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                if (Cancelled) throw new OperationCanceledException();
                Current = Path.GetFileName(f);
                try { CopyFile(f, Path.Combine(dst, Path.GetFileName(f))); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { Errors.Add(Path.GetFileName(f) + ": " + ex.Message); }
            }
            foreach (string d in Directory.GetDirectories(src))
            {
                if (Cancelled) throw new OperationCanceledException();
                CopyDirectory(d, Path.Combine(dst, Path.GetFileName(d)));
            }
            try { Directory.SetLastWriteTime(dst, Directory.GetLastWriteTime(src)); } catch { }
        }

        private void CopyFile(string src, string dst)
        {
            byte[] buf = new byte[Buffer];
            bool ok = false, created = false;
            try
            {
                using (FileStream i = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, Buffer))
                using (FileStream o = new FileStream(dst, FileMode.CreateNew, FileAccess.Write, FileShare.None, Buffer))
                {
                    created = true;
                    int n;
                    while ((n = i.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (Cancelled) throw new OperationCanceledException();
                        o.Write(buf, 0, n);
                        DoneBytes += n;
                    }
                }
                ok = true;
            }
            finally
            {
                if (created && !ok) { try { File.Delete(dst); } catch { } }   // never leave half a file behind (and never delete a file that was not ours)
            }
            try { File.SetLastWriteTime(dst, File.GetLastWriteTime(src)); } catch { }
        }

        private static void DeleteSource(string s, bool isDir)
        {
            if (isDir) Directory.Delete(s, true); else File.Delete(s);
        }
    }
}
