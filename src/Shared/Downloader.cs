using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace SchoolShield
{
    /// <summary>HTTPS downloads for updates, the AI model and the VC++ runtime. TLS 1.2, system proxy.</summary>
    internal static class Downloader
    {
        static Downloader()
        {
            // Older Windows 10 builds do not enable TLS 1.2 for .NET by default.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        }

        private static HttpWebRequest Create(string url)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "SchoolShield/2.0 (+" + Release.SiteUrl + ")";
            req.Accept = "application/vnd.github+json, application/octet-stream, */*";
            req.AllowAutoRedirect = true;
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            req.Proxy = WebRequest.DefaultWebProxy;
            if (req.Proxy != null) req.Proxy.Credentials = CredentialCache.DefaultCredentials;
            return req;
        }

        public static string GetString(string url)
        {
            using (WebResponse resp = Create(url).GetResponse())
            using (StreamReader r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        public static byte[] GetBytes(string url)
        {
            using (WebResponse resp = Create(url).GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private const int DownloadAttempts = 8;

        /// <summary>
        /// Downloads to <paramref name="dest"/> through a .part file; progress(done, total) may be null. A dropped connection (common on
        /// school Wi-Fi) does not start the file over: the download resumes from where it stopped, up to several times.
        /// </summary>
        public static void DownloadFile(string url, string dest, Action<long, long> progress)
        {
            string dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string part = dest + ".part";
            try { if (File.Exists(part)) File.Delete(part); } catch { }      // a leftover may belong to another file

            Exception last = null;
            for (int attempt = 0; attempt < DownloadAttempts; attempt++)
            {
                long have = 0;
                try { if (File.Exists(part)) have = new FileInfo(part).Length; } catch { have = 0; }
                try
                {
                    HttpWebRequest req = Create(url);
                    if (have > 0) req.AddRange(have);
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        bool resumed = have > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
                        if (!resumed) have = 0;
                        long total = resp.ContentLength > 0 ? have + resp.ContentLength : -1;
                        using (Stream src = resp.GetResponseStream())
                        using (FileStream dst = new FileStream(part, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write))
                        {
                            byte[] buf = new byte[81920];
                            long done = have;
                            int n;
                            while ((n = src.Read(buf, 0, buf.Length)) > 0)
                            {
                                dst.Write(buf, 0, n);
                                done += n;
                                if (progress != null) progress(done, total);
                            }
                            // A mirror that closes the connection early must not leave a truncated file behind.
                            if (total > 0 && done != total) throw new IOException("Загрузка оборвалась: получено " + done + " из " + total + " байт.");
                        }
                    }
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                    WebException we = ex as WebException;
                    HttpWebResponse hr = we == null ? null : we.Response as HttpWebResponse;
                    if (hr != null)
                    {
                        if (hr.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable) { try { File.Delete(part); } catch { } continue; }
                        // 404, 403 and the like will not fix themselves
                        if ((int)hr.StatusCode >= 400 && (int)hr.StatusCode < 500 && hr.StatusCode != HttpStatusCode.RequestTimeout && (int)hr.StatusCode != 429) throw;
                    }
                    if (attempt + 1 < DownloadAttempts) System.Threading.Thread.Sleep(1500 * (attempt + 1));
                }
            }
            if (last != null) throw last;
            if (File.Exists(dest)) File.Delete(dest);
            File.Move(part, dest);
        }

        public static string Sha256Hex(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Copies the AI model that came inside the installer into place. False when the file is not the right one.</summary>
        public static bool InstallModelFrom(string source)
        {
            if (!File.Exists(source) || new FileInfo(source).Length != Release.ModelSize) return false;
            if (!string.Equals(Sha256Hex(source), Release.ModelSha256, StringComparison.OrdinalIgnoreCase)) return false;
            Directory.CreateDirectory(Release.ModelDir);
            string tmp = Release.ModelPath + ".copy";
            File.Copy(source, tmp, true);
            if (File.Exists(Release.ModelPath)) File.Delete(Release.ModelPath);
            File.Move(tmp, Release.ModelPath);
            return true;
        }

        /// <summary>Installs the AI model into %ProgramData%\SchoolShield\models after checking its SHA-256.</summary>
        public static void InstallModel(Action<long, long> progress)
        {
            if (File.Exists(Release.ModelPath) && string.Equals(Sha256Hex(Release.ModelPath), Release.ModelSha256, StringComparison.OrdinalIgnoreCase))
                return;
            Directory.CreateDirectory(Release.ModelDir);
            string tmp = Release.ModelPath + ".download";
            DownloadFile(Release.ModelUrl, tmp, progress);
            string actual = Sha256Hex(tmp);
            if (!string.Equals(actual, Release.ModelSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(tmp);
                throw new InvalidDataException("Контрольная сумма модели не совпала: " + actual);
            }
            if (File.Exists(Release.ModelPath)) File.Delete(Release.ModelPath);
            File.Move(tmp, Release.ModelPath);
        }
    }

    /// <summary>RSA-SHA256 signature check (PKCS#1 v1.5) against the key embedded in the program.</summary>
    internal static class SignatureVerifier
    {
        public static bool Verify(string file, byte[] signature)
        {
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            using (FileStream fs = File.OpenRead(file))
            using (SHA256 sha = SHA256.Create())
            {
                rsa.FromXmlString(UpdateKey.PublicXml);
                byte[] hash = sha.ComputeHash(fs);
                return rsa.VerifyHash(hash, CryptoConfig.MapNameToOID("SHA256"), signature);
            }
        }
    }
}
