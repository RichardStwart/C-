using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using Shadowsocks.Controller;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json;
using System.ComponentModel;

namespace Shadowsocks.Model
{
    [Serializable]
    public class Server
    {
        public const string DefaultMethod = "chacha20-ietf-poly1305";
        public const int DefaultPort = 8388;

        #region ParseLegacyURL
        private static readonly Regex UrlFinder = new Regex(@"ss://(?<base64>[A-Za-z0-9+-/=_]+)(?:#(?<tag>\S+))?", RegexOptions.IgnoreCase);
        private static readonly Regex DetailsParser = new Regex(@"^((?<method>.+?):(?<password>.*)@(?<hostname>.+?):(?<port>\d+?))$", RegexOptions.IgnoreCase);
        #endregion ParseLegacyURL

        private const int DefaultServerTimeoutSec = 5;
        public const int MaxServerTimeoutSec = 20;

        public string server;
        public int server_port;
        public string password;
        public string method;

        // optional fields
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string plugin;
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string plugin_opts;
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string plugin_args;
        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string remarks;

        [DefaultValue("")]
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate)]
        public string group;

        public int timeout;

        public override int GetHashCode()
        {
            return server.GetHashCode() ^ server_port;
        }

        public override bool Equals(object obj)
        {
            Server o2 = (Server)obj;
            return server == o2.server && server_port == o2.server_port;
        }

        public override string ToString()
        {
            if (server.IsNullOrEmpty())
            {
                return I18N.GetString("New server");
            }

            string serverStr = $"{FormalHostName}:{server_port}";
            return remarks.IsNullOrEmpty()
                ? serverStr
                : $"{remarks} ({serverStr})";
        }

        public string GetURL(bool legacyUrl = false)
        {
            if (legacyUrl && string.IsNullOrWhiteSpace(plugin))
            {
                // For backwards compatiblity, if no plugin, use old url format
                string p = $"{method}:{password}@{server}:{server_port}";
                string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(p));
                return string.IsNullOrEmpty(remarks)
                    ? $"ss://{base64}"
                    : $"ss://{base64}#{HttpUtility.UrlEncode(remarks, Encoding.UTF8)}";
            }

            UriBuilder u = new UriBuilder("ss", null);
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{method}:{password}"));
            u.UserName = b64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
            u.Host = server;
            u.Port = server_port;
            u.Fragment = HttpUtility.UrlEncode(remarks, Encoding.UTF8);

            if (!plugin.IsNullOrWhiteSpace())
            {
                NameValueCollection param = HttpUtility.ParseQueryString("");

                string pluginPart = plugin;
                if (!string.IsNullOrWhiteSpace(plugin_opts))
                {
                    pluginPart += ";" + plugin_opts;
                }
                param["plugin"] = pluginPart;
                u.Query = param.ToString();
            }

            return u.ToString();
        }

        [JsonIgnore]
        public string FormalHostName
        {
            get
            {
                // CheckHostName() won't do a real DNS lookup
                return (Uri.CheckHostName(server)) switch
                {
                    // Add square bracket when IPv6 (RFC3986)
                    UriHostNameType.IPv6 => $"[{server}]",
                    // IPv4 or domain name
                    _ => server,
                };
            }
        }

        public Server()
        {
            server = "";
            server_port = DefaultPort;
            method = DefaultMethod;
            plugin = "";
            plugin_opts = "";
            plugin_args = "";
            password = "";
            remarks = "";
            timeout = DefaultServerTimeoutSec;
        }

        private static Server ParseLegacyURL(string ssURL)
        {
            var match = UrlFinder.Match(ssURL);
            if (!match.Success)
                return null;

            Server server = new Server();
            var base64 = match.Groups["base64"].Value.TrimEnd('/');
            var tag = match.Groups["tag"].Value;
            if (!tag.IsNullOrEmpty())
            {
                server.remarks = HttpUtility.UrlDecode(tag, Encoding.UTF8);
            }
            Match details;
            try
            {
                details = DetailsParser.Match(Encoding.UTF8.GetString(Convert.FromBase64String(
                base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='))));
            }
            catch (FormatException)
            {
                return null;
            }
            if (!details.Success)
                return null;
            server.method = details.Groups["method"].Value;
            server.password = details.Groups["password"].Value;
            server.server = details.Groups["hostname"].Value;
            server.server_port = int.Parse(details.Groups["port"].Value);
            return server;
        }

        public static Server ParseURL(string serverUrl)
        {
            string _serverUrl = serverUrl.Trim();
            if (!_serverUrl.BeginWith("ss://", StringComparison.InvariantCultureIgnoreCase))
            {
                return null;
            }

            Server legacyServer = ParseLegacyURL(serverUrl);
            if (legacyServer != null)   //legacy
            {
                return legacyServer;
            }
            else   //SIP002
            {
                Uri parsedUrl;
                try
                {
                    parsedUrl = new Uri(serverUrl);
                }
                catch (UriFormatException)
                {
                    return null;
                }
                Server server = new Server
                {
                    remarks = HttpUtility.UrlDecode(parsedUrl.GetComponents(
                        UriComponents.Fragment, UriFormat.Unescaped), Encoding.UTF8),
                    server = parsedUrl.IdnHost,
                    server_port = parsedUrl.Port,
                };

                // parse base64 UserInfo
                string rawUserInfo = parsedUrl.GetComponents(UriComponents.UserInfo, UriFormat.Unescaped);
                string base64 = rawUserInfo.Replace('-', '+').Replace('_', '/');    // Web-safe base64 to normal base64
                string userInfo;
                try
                {
                    userInfo = Encoding.UTF8.GetString(Convert.FromBase64String(
                    base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=')));
                }
                catch (FormatException)
                {
                    return null;
                }
                string[] userInfoParts = userInfo.Split(new char[] { ':' }, 2);
                if (userInfoParts.Length != 2)
                {
                    return null;
                }
                server.method = userInfoParts[0];
                server.password = userInfoParts[1];

                NameValueCollection queryParameters = HttpUtility.ParseQueryString(parsedUrl.Query);
                string[] pluginParts = (queryParameters["plugin"] ?? "").Split(new[] { ';' }, 2);
                if (pluginParts.Length > 0)
                {
                    server.plugin = pluginParts[0] ?? "";
                }

                if (pluginParts.Length > 1)
                {
                    server.plugin_opts = pluginParts[1] ?? "";
                }

                return server;
            }
        }

        public static List<Server> GetServers(string ssURL)
        {
            return ssURL
                .Split('\r', '\n', ' ')
                .Select(u => ParseURL(u))
                .Where(s => s != null)
                .ToList();
        }

        public string Identifier()
        {
            return server + ':' + server_port;
        }
    }
}
