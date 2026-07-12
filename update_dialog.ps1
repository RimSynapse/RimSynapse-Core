import os
import re

file_path = r'D:\github\RimSynapse-Core\Source\UI\Dialog_ProviderSettings.cs'

with open(file_path, 'r', encoding='utf-8') as f:
    content = f.read()

# Update TestConnectionAsync call
content = content.replace(
    "RimSynapse.Internal.HttpEngine.TestConnectionAsync(providerEnum.Value, testUrl, testKey, (success, msg) =>",
    "RimSynapse.Internal.HttpEngine.TestConnectionAsync(providerEnum.Value, testUrl, testKey, \"\", (success, msg) =>"
)

# Replace the Custom / Proxy static call and default provider with new dynamic rendering
new_section = "\""
            // Pollinations.ai doesn't use the standard fields, but we pass dummy vars
            string dummyUrl = "https://image.pollinations.ai/prompt";
            string dummyKey = "";
            string dummyModel = "";
            LlmCapabilities dummyCaps = LlmCapabilities.Image;
            DrawProviderSection(listing, "Pollinations.ai", null, ref dummyUrl, ref dummyKey, ref dummyModel, ref dummyCaps, false);

            listing.Gap(12f);
            Text.Font = GameFont.Medium;
            listing.Label("Custom Providers");
            Text.Font = GameFont.Small;
            listing.GapLine();

            for (int i = 0; i < settings.customProviders.Count; i++)
            {
                var custom = settings.customProviders[i];
                DrawProviderSection(listing, custom.name, ApiProvider.Custom, ref custom.url, ref custom.apiKey, ref custom.model, ref custom.caps, false);
                
                Rect row = listing.GetRect(24f);
                if (Widgets.ButtonText(new Rect(row.x, row.y, 100f, 24f), "Rename"))
                {
                    Find.WindowStack.Add(new Dialog_RenameCustomProvider(custom));
                }
                if (Widgets.ButtonText(new Rect(row.x + 110f, row.y, 100f, 24f), "Remove"))
                {
                    settings.customProviders.RemoveAt(i);
                    i--;
                }
                listing.Gap(12f);
            }

            if (listing.ButtonText("Add Custom Provider"))
            {
                settings.customProviders.Add(new CustomProviderSettings());
            }

            listing.Gap(24f);

            // Default Provider for testing
            string currentProviderName = settings.apiProvider.ToString().Replace("_", " ");
            listing.Label($"Default Provider: {currentProviderName}", tooltip: "This provider is used as a fallback for unrouted queries and for testing.");
            if (listing.ButtonText("Change Default Provider"))
            {
                var list = new System.Collections.Generic.List<FloatMenuOption>();
                foreach (ApiProvider provider in System.Enum.GetValues(typeof(ApiProvider)))
                {
                    ApiProvider localProvider = provider; // capture
                    string label = localProvider.ToString().Replace("_", " ");
                    list.Add(new FloatMenuOption(label, () =>
                    {
                        settings.apiProvider = localProvider;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.End();
            Widgets.EndScrollView();
        }
    }
    
    public class Dialog_RenameCustomProvider : Window
    {
        private CustomProviderSettings _provider;
        private string _newName;

        public Dialog_RenameCustomProvider(CustomProviderSettings provider)
        {
            _provider = provider;
            _newName = provider.name;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(300f, 150f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0, 0, inRect.width, 24f), "Rename Custom Provider");
            _newName = Widgets.TextField(new Rect(0, 30f, inRect.width, 24f), _newName);
            if (Widgets.ButtonText(new Rect(0, 60f, inRect.width, 24f), "Save"))
            {
                _provider.name = _newName;
                Close();
            }
        }
    }
}
"\""

old_section = "\""
            // Pollinations.ai doesn't use the standard fields, but we pass dummy vars
            string dummyUrl = "https://image.pollinations.ai/prompt";
            string dummyKey = "";
            string dummyModel = "";
            LlmCapabilities dummyCaps = LlmCapabilities.Image;
            DrawProviderSection(listing, "Pollinations.ai", null, ref dummyUrl, ref dummyKey, ref dummyModel, ref dummyCaps, false);

            DrawProviderSection(listing, "Custom / Proxy", ApiProvider.Custom, ref settings.customUrl, ref settings.customApiKey, ref settings.modelCustom, ref settings.capsCustom, false);

            // Default Provider for testing
            string currentProviderName = settings.apiProvider.ToString().Replace("_", " ");
            listing.Label($"Default Provider: {currentProviderName}", tooltip: "This provider is used as a fallback for unrouted queries and for testing.");
            if (listing.ButtonText("Change Default Provider"))
            {
                var list = new List<FloatMenuOption>();
                foreach (ApiProvider provider in System.Enum.GetValues(typeof(ApiProvider)))
                {
                    ApiProvider localProvider = provider; // capture
                    string label = localProvider.ToString().Replace("_", " ");
                    list.Add(new FloatMenuOption(label, () =>
                    {
                        settings.apiProvider = localProvider;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(list));
            }

            listing.End();
            Widgets.EndScrollView();
        }
    }
}
"\""

content = content.replace(old_section, new_section)

with open(file_path, 'w', encoding='utf-8') as f:
    f.write(content)
