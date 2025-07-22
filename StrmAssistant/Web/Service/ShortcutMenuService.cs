using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using StrmAssistant.Web.Api;
using static StrmAssistant.Web.Helper.ShortcutMenuHelper;

namespace StrmAssistant.Web.Service
{
    [Unauthenticated]
    public class ShortcutMenuService : IService, IRequiresRequest
    {
        private readonly IHttpResultFactory _resultFactory;

        public ShortcutMenuService(IHttpResultFactory resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public IRequest Request { get; set; }

        public object Get(GetStrmAssistantJs request)
        {
            return _resultFactory.GetResult(Request, StrmAssistantJsBytes, "application/x-javascript");
        }

        public object Get(GetShortcutMenu request)
        {
            return _resultFactory.GetResult(Request, ModifiedShortcutsBytes, "application/x-javascript");
        }
    }
}
