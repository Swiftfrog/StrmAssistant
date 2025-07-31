using HarmonyLib;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using System;
using System.Linq;
using static StrmAssistant.Mod.PatchManager;
using static StrmAssistant.Options.UIFunctionOptions;
using static StrmAssistant.Reflection.EmbyServerImplementations;

namespace StrmAssistant.Mod
{
    public class HidePersonNoImage : PatchBase<HidePersonNoImage>
    {
        public HidePersonNoImage()
        {
            Initialize();

            if (Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.HidePersonNoImage)
            {
                Patch();
            }
        }

        protected override void Prepare(bool apply)
        {
            PatchUnpatch(PatchTracker, apply, _attachPeople, postfix: nameof(AttachPeoplePostfix));
        }

        [HarmonyPostfix]
        private static void AttachPeoplePostfix(BaseItemDto dto, BaseItem item, DtoOptions options)
        {
            if (dto.People == null) return;

            if (!(item is Movie) && !(item is Series) && !(item is Season) && !(item is Episode)) return;

            var preference = Plugin.Instance.ExperienceEnhanceStore.GetOptions().UIFunctionOptions.HidePersonPreference;

            var noImage =
                preference?.Contains(HidePersonOption.NoImage.ToString(), StringComparison.OrdinalIgnoreCase) == true;
            var actorOnly =
                preference?.Contains(HidePersonOption.ActorOnly.ToString(), StringComparison.OrdinalIgnoreCase) == true;

            if (!noImage && !actorOnly) return;

            dto.People = dto.People.Where(p =>
                    (!noImage || p.HasPrimaryImage) &&
                    (!actorOnly || p.Type == PersonType.Actor || p.Type == PersonType.GuestStar))
                .ToArray();
        }
    }
}
