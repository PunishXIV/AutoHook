using System.Collections.Generic;

// ReSharper disable LocalizableElement

namespace AutoHook;

public static class PluginChangelog
{
    public static readonly List<Version> Versions = new()
    {
        new Version("4.3.0.0")
        {
            Main =
            {
                "UI rework (sorry for that, don't hit me)",
                " - Merged Global and Custom Presets tabs",
                " - It should be easier to see what preset is being used before starting fishing",
                " - You can edit presets without changing the currently selected one",
                "New community presets tab! You can now share your presets with others (github account required)",
                "Spearfishing now has preset import/export",
                "Options for Big-game fishing and Prize Catch for when Identical Cast >OR< Surface Slap is active",
                "Identical Cast / Surface Slap will now be used if Stop Casting is activated",
                
            },
            Minor =
            {
                "Small changes to the preset generator",
                "Spearfishing improvements soon",
                "Fixed that fucking bug where the last fish caught was not being detected, like really what the hell was wrong with that thing IT NEVER HAPPENED ON MY MACHINE i am losing my mind im crying right thanks for reading",
            }
        },
        new Version("4.2.8.3")
        {
            Main =
            {
                "Added new /ahbait command for swapping baits using names or id",
            }
        },
        new Version("4.2.8.2")
        {
            Main =
            {
                "Initial support for swimbait to be recognized as bait/mooch. This will be improved later",
                "Added optional delay before canceling a bite in the"
            },
            Minor =
            {
                "Preset List can be resized",
                "Fixed animation cancel spamming collect",
                "Fixed preset DropDownMenu, i hate ImGui"
            }
        },
        new Version("4.2.7.0")
        {
            Main =
            {
                "Lv.100 Lures updated to actually be useful",
            },
            Minor =
            {
                "Fixed auto casts not being used when Rest was used, not intended (sorry lol)"
            }
        },
        new Version("4.2.6.0")
        {
            Main =
            {
                "Added Lv. 100 Lures for bait/mooch",
                "Added Big-game Fishing",
            },
            Minor =
            {
                "I still don't know how to use Spareful Hand so i'll add it another time lol"
            }
        },
        new Version("4.2.5.0")
        {
            Main =
            {
                "AutoGig Updates",
                "Added an option to use Nature's Bounty before the fish appears while spearfishing",
                "Added an option to individually adjust the fish hitbox offset"
            }
        },
        new Version("4.2.4.0")
        {
            Main =
            {
                "7.0 Autogig support"
            }
        },
        new Version("4.2.3.3")
        {
            Main =
            {
                "New action Rest will be on unwanted bites, increasing recast speed"
            },
            Minor =
            {
                "Fixed Time Limit not working",
                "Fixed filter menu not working"
            }
        },
        new Version("4.2.3.0")
        {
            Main =
            {
                "7.0 initial support, new actions NOT included yet"
            }
        },
        new Version("4.2.2.1")
        {
            Minor =
            {
                "Localization Update"
            }
        },
        new Version("4.2.2.0")
        {
            Main =
            {
                "Update to net8",
                "Added 'use only when mooch2 is on cd' options to msb and patience."
            }
        },
        new Version("4.2.1.9")
        {
            Main =
            {
                "Added an option to only use Makeshift Bait when mooch is not available"
            }
        },
        new Version("4.2.1.8")
        {
            Minor =
            {
                "Fixed AutoGig tab not working correctly",
                "Fixed an issue with the fish counter not being reset correctly after a swap"
            }
        },
        new Version("4.2.1.6")
        {
            Minor =
            {
                "Fixed UI being resized every update (sorry)",
            }
        },
        new Version("4.2.1.5")
        {
            Minor =
            {
                "Fixed an issue with Double/Triple hook when Let Fish Escape is enabled with other conditions such as only hooking with identical cast",
            }
        },
        new Version("4.2.1.4")
        {
            Minor =
            {
                "Fixed log spam",
            }
        },
        new Version("4.2.1.3")
        {
            Minor =
            {
                "Preset Generator will include Patience and Makeshift bait if the target fish is a mooch",
                "Fixed Identical Cast/Surface Slap not being used if a preset swap happened",
            }
        },
        new Version("4.2.1.2")
        {
            Main =
            {
                "New \"Start Actions\" option, hover the Info symbol for more details",
                "[AutoCast] Added new Option to only use Fish Eyes when Makeshift Bait or Patience is active",
                "[Extra Tab] Added New Angler's Art option",
                "[Extra] Added an option to force a bait swap when starting fishing (using the new Start Fishing button or /ahstart command)",
                "[AutoGig] Added an option to gig everything"
            },
            Minor =
            {
                "[4.2.1.2] Localization Update",
                "[4.2.1.1] Fixed an issue with intuition/spectral wave tracking",
                "Fixed wrong hook being used if intuition falls off while still fishing",
                "Small Improvements to the Preset Generator",
                "Fixed an issue when swapping bait/preset while using double/triple hook",
                "Some UI changes",
            }
        },
        new Version("4.2.0.7")
        {
            Main =
            {
                "New Preset Generator (Custom Preset Tab)",
                "AutoGig Rework"
            },
            Minor =
            {
                "4.2.0.7 - Wrong text in the auto cast tab fixed",
                "4.2.0.6 - Fixed Fish Eyes recasting even when already up",
                "4.2.0.5 - You can now choose to let Cast Line cancel mooch or not (always cancelled before)",
                "Reduced the size of preset exports (by a lot)",
                "When a fish is set to Never Mooch, actions will now be able to cancel that mooching attempt",
                "Added option to not hide Extra/Autocast when they are disabled",
            }
        },
        new Version("4.1.0.8")
        {
            Minor =
            {
                "Fixed hook timers for Normal/Patience Hooks",
            }
        },
        new Version("4.1.0.7")
        {
            Minor =
            {
                "Changed some text to make the Surface Slap and Identical options on the hooking tab more clear",
            }
        },
        new Version("4.1.0.6")
        {
            Main =
            {
                "Add a separate Timeout when Chum is active",
                "Added an option to only use Thaliak's Favor when cordials are on cooldown"
            },
            Minor =
            {
                "Fixed a bug that didn't let you use both Double and Triple hook",
                "Some tooltip changes"
            }
        },
        new Version("4.1.0.5")
        {
            Minor =
            {
                "Fixed typo: Use Mooch Timer > Use Chum Timer",
            }
        },
        new Version("4.1.0.4")
        {
            Main =
            {
                "Another rework on the hooking configuration",
                "Each Bite (!, !!, !!!) has its own configuration",
                "You can now make a separate hooking config for intuition"
            },
            Minor =
            {
                "Bait/Mooch tab were moved to a new Hooking tab",
                "Default Preset was renamed to Global Preset",
                "Added an option to swap the UI style (just a bit) in the config / guides tab",
                "Added an option to cast cordial outside of the specified time window",
            }
        },
        new Version("4.0.0.8")
        {
            Minor =
            {
                "Applied logic to AutoCordial, AutoFishEyes, AutoMakeShiftBait and Auto Patience so they are only cast when casting line is a valid action",
                "Added logging to try and debug Spearfishing problems"
            }
        },
        new Version("4.0.0.7")
        {
            Minor =
            {
                "Fixed an issue swapping presets if the fish escaped/not hooked",
                "Fixed an issue when trying to add new presets without changing the default name of existing ones (New Preset 1,2,3...)"
            }
        },
        new Version("4.0.0.6")
        {
            Minor =
            {
                "Issue where clicking a button in the custom Mooch tab would corrupt all fonts has been fixed.",
                "Fixed the letter case for Makeshift Bait"
            }
        },
        new Version("4.0.0.5")
        {
            Main =
            {
                "Auto Casts can now be re-ordered to be used in a different priority",
                "Allows bait to be imported from selected bait in game",
                "Allow casting only within specific Eorzea times"
            },
            Minor =
            {
                "Allow to stop/quit fishing after intuition is lost",
                "Fixed issue where certain actions would still be used if the plugin was disabled"
            }
        },
        new Version("4.0.0.4")
        {
            Main =
            {
                "Allows preset menu to be shown as a sidebar rather than dropdown",
                "Allows fish configs to be ignored if fishing intuition is active",
                "Identical Cast\r\n" +
                "- - Added an option to only use if cordial is available\r\n" +
                "- - Added an option to only use after the fish is caught X amount of times.\r\n" +
                "- Bait & Mooch Tabs\r\n" +
                "- -Added an option to reset the caught counter under \"stop fishing\"\r\n" +
                "- Extra Tab\r\n" +
                "- -Added an option to reset the counter when a preset swap happens."
            },
            Minor =
            {
                "Fix spectral current settings not working."
            }
        },
        new Version("4.0.0.3")
        {
            Main =
            {
                "Enabling Extra Casts or Auto Casts in a preset will disable the corresponding config in the opposite kind of preset (i.e enabling Default Auto Casts will disable Custom Auto Casts and vice versa)"
            },
            Minor =
            {
                @"Fix new ""Use Cordials before Thaliak's Favor"" setting",
                "Allow setting GP threshold for Thaliak's Favor down to 0 instead of 3"
            }
        },
        new Version("4.0.0.2")
        {
            Main =
            {
                "Added option to use Cordials before Thaliak's Favor to the Thaliak's Favor config",
                "Allow Cordial to overcap GP if Identical Cast is active"
            }
        },
        new Version("4.0.0.1")
        {
            Minor =
            {
                "Fix issue with Collector's Glove not activating"
            }
        },
        new Version("4.0.0.0")
        {
            Main =
            {
                "Migration to Puni.sh repo"
            }
        },
        new Version("3.0.4.0")
        {
            Main =
            {
                "(by Jaksuhn) Added auto cast collector's glove",
                "(by Jaksuhn) Added option to refresh Patience early",
            }
        },
        new Version("3.0.3.0")
        {
            Main =
            {
                "(by Jaksuhn) Added swap preset/bait on Spectral Currents",
                "(by Jaksuhn) Added more Surface Slap options",
                "(by Jaksuhn) Added option to chum only when intuition duration is greater than x seconds",
            },
            Minor =
            {
                "Fixed an issue with swapping both preset and bait at the same time",
                "More IPC options"
            }
        },
        new Version("3.0.2.0")
        {
            Main =
            {
                "(by Jaksuhn) Added IPC",
                "(by Jaksuhn) Added makeshift bait only under intuition option"
            },
            Minor =
            {
                "Added a new command to change the current preset"
            }
        },
        new Version("3.0.1.0")
        {
            Main =
            {
                "Added new sub-tab 'Extra' for extra options",
                "Added options to change bait/presets when gaining/losing intuition",
                "(Config) Added optional delay for hooking or auto casting",
            },
            Minor =
            {
                "Patience I/II has priority over MakeShift Bait if both options are enabled",
                "Added a new command to open the plugin menu",
                "Minor text changes"
            }
        },
        new Version("3.0.0.0")
        {
            Main =
            {
                "Major plugin rework to try and support complex conditions",
                "AutoCasts are now preset based, you can now have multiple presets with different AutoCasts",
                "Merged AutoCast and Gp Config into a single tab",
                "Bait and Mooch hook configs are now separated into different tabs for better organization",
                "Added a new 'Fish' Tab, which contains new options related to fish caught",
                "Its now possible to change the current bait (or preset) when a fish is caught X times",
                "Localization Updates"
            },
            Minor =
            {
                "Fixed localization issues",
                "Fixed AutoCast not working if not hooking after a bite"
            }
        },
        new Version("2.5.0.0")
        {
            Main =
            {
                "Added localization for Chinese, French, German,Japanese and Korean",
                "API9 update"
            }
        },
        new Version("2.4.4.0")
        {
            Main =
            {
                "It's now possible to enable both Double and Triple hook (hold shift when selecting the options)",
            },
            Minor =
            {
                "Removed capitalization for bait names",
            }
        },
        new Version("2.4.3.0")
        {
            Main =
            {
                "Added Watered Cordials for AutoCasts"
            },
            Minor =
            {
                "Fixed duplicated GP Configs"
            }
        },
        new Version("2.4.2.0")
        {
            Main =
            {
                "Added customizable hitbox for autogig",
                "Added an option to see the fish hitbox when spearfishing",
                "(experimental) Nature's Bounty will be used when the target fish appears on screen",
                "Added changelog button"
            },
            Minor =
            {
                "Gig hitbox is now enabled by default",
                "Fixed the order of the Chum Timer Min/Max fields",
                "Fixed some options not saving correctly"
            }
        },
        new Version("2.4.1.0")
        {
            Main = { "Added options to cast Mooch only when under the effect of Fisher's Intuition" }
        },
        new Version("2.4.0.0")
        {
            Main =
            {
                "Presets for custom baits added, you can now swap configs without needing to recreate it every time",
                "Added options to cast Chum only when under the effect of Fisher's Intuition",
                "Added an option to only cast Prize Catch when Mooch II is not available, saving you 100gp if all you want is to mooch",
                "Added Custom Timer when under the effect of Chum",
                "Added an option to only use Prize Catch when under the effect of Identical Cast",
                "Upgrade to .net7 and API8"
            }
        }
    };

    public class Version
    {
        public string VersionNumber { get; set; }
        public List<string> Main { get; set; }
        public List<string> Minor { get; set; }

        public Version(string versionNumber)
        {
            VersionNumber = versionNumber;
            Main = new List<string>();
            Minor = new List<string>();
        }
    }
}