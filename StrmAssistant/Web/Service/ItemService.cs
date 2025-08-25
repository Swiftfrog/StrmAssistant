using MediaBrowser.Controller.Api;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using StrmAssistant.Web.Api;
using System;
using System.Collections.Generic;
using System.Threading;

namespace StrmAssistant.Web.Service
{
    public class ItemService : BaseApiService
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IProviderManager _providerManager;

        public ItemService(ILibraryManager libraryManager, IProviderManager providerManager)
        {
            _libraryManager = libraryManager;
            _providerManager = providerManager;
        }

        public void Post(LockItem request)
        {
            var itemById = _libraryManager.GetItemById(request.ItemId);

            var items = _libraryManager.GetItemList(new InternalItemsQuery
            {
                PresentationUniqueKey = itemById.PresentationUniqueKey
            });

            var requestLock = request.LockData;

            foreach (var item in items)
            {
                var updatedItems = new List<BaseItem>();

                if (UpdateItemLockStatus(item, requestLock))
                {
                    updatedItems.Add(item);
                }

                if (item is Folder folder)
                {
                    foreach (var child in folder.GetItemList(new InternalItemsQuery { Recursive = true }))
                    {
                        if (UpdateItemLockStatus(child, requestLock))
                        {
                            updatedItems.Add(child);
                        }
                    }
                }

                if (updatedItems.Count > 0)
                {
                    _libraryManager.UpdateItems(updatedItems, null, ItemUpdateType.MetadataEdit, true, false, null,
                        CancellationToken.None);

                    foreach (var itemToUpdate in updatedItems)
                    {
                        _providerManager.SaveMetadata(itemToUpdate, ItemUpdateType.MetadataEdit);
                    }
                }
            }
        }

        private static bool UpdateItemLockStatus(BaseItem item, bool requestLock)
        {
            var updated = false;
            var currentLock = item.IsLocked;

            if (currentLock != requestLock)
            {
                item.IsLocked = requestLock;
                updated = true;
            }

            if (!requestLock && item.LockedFields.Length > 0)
            {
                item.LockedFields = Array.Empty<MetadataFields>();
                updated = true;
            }

            return updated;
        }
    }
}
