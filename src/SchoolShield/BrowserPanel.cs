using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SchoolShield
{
    /// <summary>Is the Microsoft Edge WebView2 runtime installed? (It ships with Edge and with current Windows 10.)</summary>
    internal static class WebView2Probe
    {
        private static bool? available;

        public static bool Available
        {
            get
            {
                if (available == null)
                {
                    try { available = !string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString()); }
                    catch { available = false; }
                    Log.Write("web", "WebView2 runtime: " + (available.Value ? "найден" : "не найден"));
                }
                return available.Value;
            }
        }
    }

    /// <summary>
    /// The student's browser, built into the launcher. Unlike Chrome/Edge it has no developer tools, no extensions,
    /// no downloads, no address-bar tricks and no way to switch the ad blur off, and it applies the school's site lists
    /// to every page. Ads are blocked at the network level (ad networks) and blurred on the page (what remains).
    /// </summary>
    internal sealed class BrowserPanel : UserControl
    {
        private static readonly HashSet<string> MenuAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "back", "forward", "reload", "copy", "cut", "paste", "selectAll", "undo", "redo"
        };

        private readonly MainForm main;
        private readonly Config cfg;
        private readonly WebView2 web = new WebView2();
        private readonly Panel bar = new Panel();
        private readonly MotionButton back = new MotionButton(BtnKind.Secondary, "", null);
        private readonly MotionButton forward = new MotionButton(BtnKind.Secondary, "", null);
        private readonly MotionButton reload = new MotionButton(BtnKind.Secondary, "", null);
        private readonly MotionButton home = new MotionButton(BtnKind.Secondary, "", null);
        private readonly TextBox address = new TextBox();
        private readonly Label shield = new Label();
        private WebPolicy policy;
        private CoreWebView2 core;
        private bool started;
        private bool internalNav;
        private string pendingUrl;

        public BrowserPanel(MainForm main)
        {
            this.main = main;
            cfg = main.Cfg;
            Dock = DockStyle.Fill;
            BackColor = Theme.Background;

            bar.Dock = DockStyle.Top;
            bar.Height = 48;
            bar.BackColor = Theme.Sidebar;

            Tool(back, "", 8, "Назад", delegate { if (core != null && core.CanGoBack) core.GoBack(); });
            Tool(forward, "", 52, "Вперёд", delegate { if (core != null && core.CanGoForward) core.GoForward(); });
            Tool(reload, "", 96, "Обновить", delegate { if (core != null) core.Reload(); });
            Tool(home, "", 140, "Домой", delegate { GoHome(); });

            address.Font = Theme.UiFont(12);
            address.BackColor = Theme.Card;
            address.ForeColor = Theme.Text;
            address.BorderStyle = BorderStyle.FixedSingle;
            address.SetBounds(188, 10, 400, 28);
            address.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            address.KeyDown += delegate (object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && policy != null)
                {
                    e.SuppressKeyPress = true;
                    Navigate(policy.ResolveInput(address.Text));
                }
            };

            shield.Text = cfg.Browser.AdBlur ? "  реклама размыта" : "";
            shield.Font = Theme.UiFont(9.5f);
            shield.ForeColor = Theme.Muted;
            shield.TextAlign = System.Drawing.ContentAlignment.MiddleRight;
            shield.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            shield.SetBounds(0, 10, 180, 28);

            bar.Controls.Add(back);
            bar.Controls.Add(forward);
            bar.Controls.Add(reload);
            bar.Controls.Add(home);
            bar.Controls.Add(address);
            bar.Controls.Add(shield);
            bar.Resize += delegate
            {
                shield.Left = bar.Width - shield.Width - 12;
                address.Width = Math.Max(120, shield.Left - address.Left - 10);
            };

            web.Dock = DockStyle.Fill;
            web.DefaultBackgroundColor = Theme.Background;
            Controls.Add(web);
            Controls.Add(bar);
        }

        private void Tool(Button b, string glyph, int x, string tip, EventHandler click)
        {
            b.Text = glyph;
            b.Font = Theme.IconFont(13);
            b.AutoSize = false;
            b.Cursor = Cursors.Hand;
            b.SetBounds(x, 6, 40, 36);
            b.Click += click;
            new ToolTip().SetToolTip(b, tip);
        }

        // ------------------------------------------------------------------ start

        public async void EnsureStarted()
        {
            if (started) return;
            started = true;
            try
            {
                policy = WebPolicy.Load(cfg);
                Log.Write("web", "Политика сайтов: доменов " + policy.BlockedDomains + ", рекламных хостов " + policy.AdHostCount);
                string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SchoolShield", "WebView2");
                Directory.CreateDirectory(dataDir);

                CoreWebView2EnvironmentOptions opts = new CoreWebView2EnvironmentOptions();
                opts.Language = "ru";
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, dataDir, opts);
                await web.EnsureCoreWebView2Async(env);
                core = web.CoreWebView2;

                Configure(env);
                if (cfg.Browser.AdBlur) await core.AddScriptToExecuteOnDocumentCreatedAsync(BuildBlurScript());

                if (!string.IsNullOrEmpty(pendingUrl)) Navigate(pendingUrl);
                else GoHome();
            }
            catch (Exception ex)
            {
                Log.Write("web", "Браузер не запустился: " + ex);
                started = false;
                ShowFailure(ex.Message);
            }
        }

        private void ShowFailure(string message)
        {
            Label l = new Label();
            l.Text = "Не удалось запустить встроенный браузер.\n" + message + "\n\nСообщите учителю.";
            l.ForeColor = Theme.Text;
            l.Font = Theme.UiFont(13);
            l.Dock = DockStyle.Fill;
            l.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            Controls.Add(l);
            l.BringToFront();
        }

        private string BuildBlurScript()
        {
            string js;
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("adblur.js"))
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                js = r.ReadToEnd();
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["videoHosts"] = cfg.Browser.VideoSites;
            c["adHosts"] = new List<string>(policy.AdHostList);
            return "window.__ssCfg=" + new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(c) + ";\n" + js;
        }

        // ------------------------------------------------------------------ rules

        private void Configure(CoreWebView2Environment env)
        {
            CoreWebView2Settings st = core.Settings;
            st.AreDevToolsEnabled = false;                 // no F12, no "Inspect": the blur cannot be removed from the page
            st.AreBrowserAcceleratorKeysEnabled = false;   // no Ctrl+U, Ctrl+S, Ctrl+P, F12, Ctrl+Shift+I ...
            st.AreDefaultContextMenusEnabled = true;       // trimmed down in ContextMenuRequested
            st.IsStatusBarEnabled = false;
            st.IsGeneralAutofillEnabled = false;
            st.IsPasswordAutosaveEnabled = false;
            st.IsSwipeNavigationEnabled = false;
            st.IsWebMessageEnabled = false;
            st.AreHostObjectsAllowed = false;

            core.NavigationStarting += OnNavigationStarting;
            core.FrameNavigationStarting += OnFrameNavigationStarting;
            core.NewWindowRequested += OnNewWindow;
            core.DownloadStarting += OnDownload;
            core.PermissionRequested += delegate (object s, CoreWebView2PermissionRequestedEventArgs e)
            {
                e.State = CoreWebView2PermissionState.Deny;
                e.Handled = true;
            };
            core.ContextMenuRequested += OnContextMenu;
            core.DocumentTitleChanged += delegate { CheckTitle(); };
            core.SourceChanged += delegate
            {
                string src = core.Source ?? "";
                if (!src.StartsWith("data:")) address.Text = src;
                UpdateButtons();
            };
            core.NavigationCompleted += delegate { internalNav = false; UpdateButtons(); CheckTitle(); };
            core.ProcessFailed += delegate { BeginInvoke(new Action(delegate { try { core.Reload(); } catch { } })); };

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += delegate (object s, CoreWebView2WebResourceRequestedEventArgs e)
            {
                try
                {
                    string reqHost = WebPolicy.HostOf(e.Request.Uri);
                    string pageHost = WebPolicy.HostOf(core.Source ?? "");
                    if (policy.IsAdRequest(reqHost, pageHost))
                    {
                        e.Response = env.CreateWebResourceResponse(null, 204, "No Content", "");
                        return;
                    }
                    if (!cfg.IsTeacher() && policy.IsYouTubeHost(reqHost)) e.Request.Headers.SetHeader("YouTube-Restrict", "Strict");   // YouTube Restricted Mode
                }
                catch { /* never break page loading */ }
            };
        }

        private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            NavDecision d = policy.Check(e.Uri, true, internalNav && e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase));
            if (!d.Allowed)
            {
                e.Cancel = true;
                Deny(d, e.Uri);
                return;
            }
            Uri u;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out u))
            {
                Uri safe = policy.ApplySafeSearch(u);
                if (!object.ReferenceEquals(safe, u))
                {
                    e.Cancel = true;
                    string target = safe.AbsoluteUri;
                    BeginInvoke(new Action(delegate { core.Navigate(target); }));
                }
            }
        }

        private void OnFrameNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            NavDecision d = policy.Check(e.Uri, false, false);
            if (!d.Allowed) { e.Cancel = true; return; }
            if (policy.IsAdRequest(WebPolicy.HostOf(e.Uri), WebPolicy.HostOf(core.Source ?? ""))) e.Cancel = true;
        }

        private void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;                      // no pop-ups, no new windows
            if (!e.IsUserInitiated) return;        // ad pop-ups are never user-initiated
            string uri = e.Uri;
            BeginInvoke(new Action(delegate { Navigate(uri); }));
        }

        private void OnDownload(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            if (cfg.Browser.AllowDownloads) return;
            e.Cancel = true;
            e.Handled = true;
            Log.Write("web", "Скачивание заблокировано: " + e.DownloadOperation.Uri);
            string file = "";
            try { file = System.IO.Path.GetFileName(new Uri(e.DownloadOperation.Uri).AbsolutePath); } catch { }
            Ui.Violation(ViolationKind.Download, file);
        }

        private void OnContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            IList<CoreWebView2ContextMenuItem> items = e.MenuItems;
            for (int i = items.Count - 1; i >= 0; i--)
                if (!MenuAllowed.Contains(items[i].Name ?? "")) items.RemoveAt(i);
        }

        private void CheckTitle()
        {
            try
            {
                string title = core.DocumentTitle;
                if (policy.IsGameText(title))
                {
                    Log.Write("web", "Игровая страница закрыта: " + title);
                    Ui.Attempts++;
                    ShowHtml(policy.BlockedPage(ViolationKind.Game, "Игры сейчас запрещены администратором.", title, Theme.Dark, Ui.Attempts));
                    return;
                }
                if (!policy.IsBadText(title)) return;
                Log.Write("web", "Запрещённое слово в заголовке страницы: " + title);
                Ui.Attempts++;
                ShowHtml(policy.BlockedPage(ViolationKind.Adult, "На странице обнаружено запрещённое содержимое.", "", Theme.Dark, Ui.Attempts));
            }
            catch { }
        }

        private void Deny(NavDecision d, string uri)
        {
            Log.Write("web", "Заблокировано: " + uri + " — " + d.Reason);
            Ui.Attempts++;
            string html = policy.BlockedPage(d.Kind, d.Reason, d.Host, Theme.Dark, Ui.Attempts);
            BeginInvoke(new Action(delegate { ShowHtml(html); }));
        }

        private void UpdateButtons()
        {
            if (core == null) return;
            back.Enabled = core.CanGoBack;
            forward.Enabled = core.CanGoForward;
        }

        // ------------------------------------------------------------------ public

        public void Navigate(string url)
        {
            if (core == null) { pendingUrl = url; EnsureStarted(); return; }
            NavDecision d = policy.Check(url, true, false);
            if (!d.Allowed) { Deny(d, url); return; }
            core.Navigate(url);
        }

        public void GoHome()
        {
            string h = cfg.Browser.HomePage;
            if (string.IsNullOrEmpty(h) || h == "start") ShowHtml(policy.StartPage(cfg, Theme.Dark));
            else Navigate(h);
        }

        /// <summary>Own pages (start page, "blocked" page). The only place where data: addresses are accepted.</summary>
        private void ShowHtml(string html)
        {
            if (core == null) return;
            internalNav = true;
            core.NavigateToString(html);
        }

        /// <summary>Called when the screen filter found something unacceptable: drop the page.</summary>
        public void Blank()
        {
            if (core == null) return;
            try { core.Navigate("about:blank"); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { web.Dispose(); } catch { } }
            base.Dispose(disposing);
        }
    }
}
