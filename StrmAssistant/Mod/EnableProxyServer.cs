using Emby.Web.GenericEdit.Elements;
using HarmonyLib;
using System;
using System.Net;
using System.Net.Http;
using static StrmAssistant.Common.CommonUtility;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod
{
    public class EnableProxyServer : PatchBase<EnableProxyServer>
    {
        private static readonly string[] BypassAddressList =
        {
            "10.*", "172.1[6-9].*", "172.2[0-9].*", "172.3[0-1].*", "192.168.*"
        };

        public EnableProxyServer()
        {
            if (Plugin.Instance.MainOptionsStore.GetOptions().NetworkOptions.EnableProxyServer)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _createHttpClientHandler,
                postfix: nameof(CreateHttpClientHandlerPostfix));
        }

        [HarmonyPostfix]
        private static void CreateHttpClientHandlerPostfix(HttpMessageHandler __result)
        {
            var options = Plugin.Instance.MainOptionsStore.PluginOptions.NetworkOptions;
            var proxyStatus = options.ProxyServerStatus.Status;
            var ignoreCertificateValidation = options.IgnoreCertificateValidation;

            if (Uri.TryCreate(options.ProxyServerUrl, UriKind.Absolute, out var proxyUri) &&
                proxyStatus == ItemStatus.Succeeded && TryParseProxyUrl(options.ProxyServerUrl, out var schema,
                    out var host, out var port, out var username, out var password))
            {
                var proxy = new WebProxy(proxyUri)
                {
                    BypassProxyOnLocal = true,
                    BypassList = BypassAddressList,
                    Credentials = !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password)
                        ? new NetworkCredential(username, password)
                        : null
                };

                if (__result is HttpClientHandler httpClientHandler)
                {
                    httpClientHandler.Proxy = proxy;
                    httpClientHandler.UseProxy = true;
                    if (ignoreCertificateValidation)
                    {
                        httpClientHandler.ServerCertificateCustomValidationCallback =
                            (httpRequestMessage, cert, chain, sslErrors) => true;
                    }
                }
                else if (__result is SocketsHttpHandler socketsHttpHandler)
                {
                    socketsHttpHandler.Proxy = proxy;
                    socketsHttpHandler.UseProxy = true;
                    if (ignoreCertificateValidation)
                    {
                        socketsHttpHandler.SslOptions.RemoteCertificateValidationCallback =
                            (sender, cert, chain, sslErrors) => true;
                    }
                }
            }
        }
    }
}
