using System;
using System.Text;

namespace SchoolShield
{
    /// <summary>
    /// The page the built-in browser shows instead of a blocked site: a pulsing picture, a joke, what was caught, a way back.
    /// Pure CSS animation (no scripts), so it works in the locked-down browser. Colours follow the launcher theme.
    /// </summary>
    internal static class BlockPage
    {
        private static string Hex(System.Drawing.Color c) { return "#" + c.R.ToString("x2") + c.G.ToString("x2") + c.B.ToString("x2"); }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        /// <summary>Stroke pictures in a 64x64 box, one per kind of violation.</summary>
        private static string Svg(ViolationKind kind)
        {
            string body;
            switch (kind)
            {
                case ViolationKind.Site:
                    body = "<circle cx='32' cy='32' r='22'/><ellipse cx='32' cy='32' rx='9' ry='22'/><path d='M10 32h44'/><path d='M12 12l40 40' class='sl'/>";
                    break;
                case ViolationKind.Download:
                    body = "<path d='M32 8v30'/><path d='M20 28l12 12 12-12'/><path d='M10 44v10h44V44'/><path d='M12 12l40 40' class='sl'/>";
                    break;
                case ViolationKind.Game:
                    body = "<rect x='6' y='20' width='52' height='30' rx='14'/><path d='M20 29v12M14 35h12'/><circle cx='42' cy='31' r='2.5' class='fl'/><circle cx='49' cy='38' r='2.5' class='fl'/><path d='M12 12l40 40' class='sl'/>";
                    break;
                case ViolationKind.Adult:
                    body = "<circle cx='32' cy='32' r='24'/><text x='32' y='39' text-anchor='middle' font-size='20' font-weight='700' class='fl' stroke='none'>18+</text><path d='M13 13l38 38' class='sl'/>";
                    break;
                case ViolationKind.Program:
                    body = "<rect x='6' y='10' width='52' height='44' rx='6'/><path d='M6 22h52'/><path d='M24 32l16 16M40 32L24 48'/>";
                    break;
                case ViolationKind.File:
                    body = "<path d='M16 6h26l14 14v38H16z'/><path d='M42 6v14h14'/><path d='M24 36h20M24 46h14'/><path d='M12 12l40 40' class='sl'/>";
                    break;
                case ViolationKind.Folder:
                    body = "<path d='M6 16h20l6 8h26v30H6z'/><circle cx='32' cy='38' r='5'/>";
                    break;
                case ViolationKind.Wifi:
                    body = "<path d='M10 28a30 30 0 0 1 44 0M18 36a20 20 0 0 1 28 0M26 44a9 9 0 0 1 12 0'/><circle cx='32' cy='52' r='2.5' class='fl'/><path d='M12 12l40 40' class='sl'/>";
                    break;
                default:
                    body = "<circle cx='32' cy='32' r='24'/><path d='M32 18v18'/><circle cx='32' cy='46' r='2.5' class='fl'/>";
                    break;
            }
            return "<svg viewBox='0 0 64 64' width='96' height='96' fill='none' stroke='currentColor' stroke-width='3.4' stroke-linecap='round' stroke-linejoin='round'>" + body + "</svg>";
        }

        public static string Html(ViolationKind kind, Joke joke, string reason, string host, bool dark, bool animate, int attempt)
        {
            string bg = dark ? Hex(Theme.Background) : "#f3f6fc";
            string card = dark ? "#131a30" : "#ffffff";
            string text = dark ? "#e8edf8" : "#0f172a";
            string muted = dark ? "#8c98b2" : "#5f6e87";
            string accent = Hex(ViolationForm.AccentOf(kind));
            string btnText = dark ? "#06101f" : "#ffffff";
            string accentBtn = dark ? "#22d3ee" : "#0891b2";

            StringBuilder css = new StringBuilder();
            css.Append("*{box-sizing:border-box}html,body{margin:0;height:100%;background:" + bg + ";color:" + text + ";font:16px 'Segoe UI',system-ui,sans-serif;overflow:hidden}");
            css.Append("body{display:flex;align-items:center;justify-content:center;background:radial-gradient(900px 520px at 18% 12%," + accent + "26,transparent 70%),radial-gradient(800px 500px at 85% 95%," + accentBtn + "1f,transparent 70%)," + bg + "}");
            css.Append(".w{position:relative;max-width:720px;padding:44px 40px;text-align:center}");
            css.Append(".ic{position:relative;display:inline-flex;align-items:center;justify-content:center;width:150px;height:150px;border-radius:44px;background:" + accent + "22;color:" + accent + ";box-shadow:inset 0 0 0 2px " + accent + "55}");
            css.Append(".ic svg{position:relative;z-index:2}.sl{stroke:" + accent + "}.fl{fill:currentColor}");
            css.Append(".r{position:absolute;inset:0;border-radius:44px;box-shadow:0 0 0 2px " + accent + "}");
            css.Append("h1{font-size:40px;line-height:1.15;margin:30px 0 10px;letter-spacing:-.5px}");
            css.Append("p.l{font-size:21px;color:" + muted + ";margin:0 0 20px;line-height:1.4}");
            css.Append("p.k{font-size:15px;color:" + accent + ";margin:0 0 18px;font-style:italic}");
            css.Append(".c{display:inline-block;margin:0 6px 24px;padding:7px 16px;border-radius:99px;background:" + accent + "22;color:" + accent + ";font-size:14px;max-width:90%;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;vertical-align:bottom}");
            css.Append(".b{display:inline-block;padding:13px 30px;border-radius:14px;background:linear-gradient(90deg," + accentBtn + ",#818cf8);color:" + btnText + ";text-decoration:none;font-weight:700;font-size:17px;transition:transform .18s,box-shadow .18s}");
            css.Append(".b:hover{transform:translateY(-3px);box-shadow:0 12px 30px " + accentBtn + "55}.b:active{transform:scale(.96)}");
            css.Append(".n{position:fixed;left:0;right:0;bottom:18px;color:" + muted + ";font-size:13px;text-align:center}");
            if (animate)
            {
                css.Append("@keyframes pop{0%{transform:scale(.3) rotate(-14deg);opacity:0}55%{transform:scale(1.14) rotate(5deg);opacity:1}100%{transform:scale(1) rotate(0)}}");
                css.Append("@keyframes ring{0%{transform:scale(1);opacity:.55}100%{transform:scale(1.7);opacity:0}}");
                css.Append("@keyframes shake{0%,100%{transform:translateX(0) rotate(0)}15%{transform:translateX(-9px) rotate(-5deg)}30%{transform:translateX(8px) rotate(4deg)}45%{transform:translateX(-6px) rotate(-3deg)}60%{transform:translateX(5px) rotate(2deg)}75%{transform:translateX(-2px)}}");
                css.Append("@keyframes rise{0%{transform:translateY(22px);opacity:0}100%{transform:none;opacity:1}}");
                css.Append("@keyframes drift{0%,100%{background-position:0 0,0 0,0 0}50%{background-position:30px 20px,-30px -20px,0 0}}");
                css.Append("body{background-size:130% 130%,130% 130%,auto;animation:drift 14s ease-in-out infinite}");
                css.Append(".ic{animation:pop .8s cubic-bezier(.2,.9,.3,1.2) both}.ic svg{animation:shake .7s .75s both}");
                css.Append(".r{animation:ring 2.4s ease-out infinite}.r+.r{animation-delay:1.2s}");
                css.Append("h1{animation:rise .6s .25s both}p.l{animation:rise .6s .4s both}p.k{animation:rise .6s .55s both}.c{animation:rise .6s .6s both}.b{animation:rise .6s .75s both}");
                css.Append("@media (prefers-reduced-motion:reduce){*{animation:none!important}}");
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"ru\"><head><meta charset=\"utf-8\"><title>Недоступно</title><style>").Append(css).Append("</style></head><body><div class=\"w\">");
            sb.Append("<div class=\"ic\"><span class=\"r\"></span><span class=\"r\"></span>").Append(Svg(kind)).Append("</div>");
            sb.Append("<h1>").Append(Esc(joke.Title)).Append("</h1>");
            sb.Append("<p class=\"l\">").Append(Esc(joke.Line)).Append("</p>");
            if (!string.IsNullOrEmpty(joke.Remark)) sb.Append("<p class=\"k\">").Append(Esc(joke.Remark)).Append("</p>");
            if (!string.IsNullOrEmpty(host)) sb.Append("<span class=\"c\">").Append(Esc(host)).Append("</span>");
            if (!string.IsNullOrEmpty(reason)) sb.Append("<span class=\"c\">").Append(Esc(reason)).Append("</span>");
            sb.Append("<br><a class=\"b\" href=\"javascript:history.back()\">Назад</a></div>");
            sb.Append("<div class=\"n\">Если вы считаете, что это ошибка, скажите учителю.").Append(attempt >= 2 ? " Попытка №" + attempt + "." : "").Append("</div></body></html>");
            return sb.ToString();
        }
    }
}
