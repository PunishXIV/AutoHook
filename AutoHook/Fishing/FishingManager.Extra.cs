using System.Linq;
using AutoHook.Configurations;
using AutoHook.Data;
using AutoHook.Enums;
using AutoHook.SeFunctions;
using AutoHook.Utils;

namespace AutoHook.Fishing;

public partial class FishingManager
{
    public ExtraConfig GetExtraCfg()
    {
        return Presets.SelectedPreset?.ExtraCfg.Enabled ?? false
            ? Presets.SelectedPreset.ExtraCfg
            : Presets.DefaultPreset.ExtraCfg;
    }

    private void CheckExtraActions(ExtraConfig extraCfg)
    {
        CheckIntuition(extraCfg);
        CheckSpectral(extraCfg);
        CheckAnglersArt(extraCfg);
    }

    private void CheckSpectral(ExtraConfig extraCfg)
    {
        if (_spectralCurrentStatus == SpectralCurrentStatus.NotActive)
        {
            if (!PlayerRes.IsInActiveSpectralCurrent())
                return;

            _spectralCurrentStatus = SpectralCurrentStatus.Active;

            if (!extraCfg.Enabled)
                return;

            // Check if the preset was already swapped
            if (extraCfg.SwapPresetSpectralCurrentGain && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
            {
                var preset =
                    Presets.CustomPresets.FirstOrDefault(preset =>
                        preset.PresetName == extraCfg.PresetToSwapSpectralCurrentGain);

                _lastStep |= FishingSteps.PresetSwapped; // one try
                if (preset != null)
                {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Active: Swapping preset to {extraCfg.PresetToSwapSpectralCurrentGain}");
                    Service.Save();
                }
                else
                    Service.PrintChat(@$"Preset {extraCfg.PresetToSwapSpectralCurrentGain} not found.");
            }

            // Check if the bait was already swapped
            if (extraCfg.SwapBaitSpectralCurrentGain && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
            {
                var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapSpectralCurrentGain);

                _lastStep |= FishingSteps.BaitSwapped; // one try
                if (result == BaitManager.ChangeBaitReturn.Success)
                {
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Active: Swapping bait to {extraCfg.BaitToSwapSpectralCurrentGain.Name}");
                    Service.Save();
                }
            }
        }

        if (_spectralCurrentStatus == SpectralCurrentStatus.Active)
        {
            if (PlayerRes.IsInActiveSpectralCurrent())
                return;

            _spectralCurrentStatus = SpectralCurrentStatus.NotActive;

            // Check if the preset was already swapped
            if (!extraCfg.Enabled)
                return;

            if (extraCfg.SwapPresetSpectralCurrentLost && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
            {
                var preset =
                    Presets.CustomPresets.FirstOrDefault(preset =>
                        preset.PresetName == extraCfg.PresetToSwapSpectralCurrentLost);

                _lastStep |= FishingSteps.PresetSwapped; // one try

                if (preset != null)
                {
                    Service.Save();
                    Presets.SelectedPreset = preset;
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Ended: Swapping preset to {extraCfg.SwapPresetSpectralCurrentLost}");
                    Service.Save();
                }
                else
                    Service.PrintChat(@$"Preset {extraCfg.SwapPresetSpectralCurrentLost} not found.");
            }

            // Check if the bait was already swapped
            if (extraCfg.SwapBaitSpectralCurrentLost && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
            {
                var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapSpectralCurrentLost);

                _lastStep |= FishingSteps.BaitSwapped; // one try

                if (result == BaitManager.ChangeBaitReturn.Success)
                {
                    Service.PrintChat(
                        @$"[Extra] Spectral Current Ended: Swapping bait to {extraCfg.BaitToSwapSpectralCurrentLost.Name}");
                    Service.Save();
                }
            }
        }
    }

    private void CheckIntuition(ExtraConfig extraCfg)
    {
        if (IntuitionStatus == IntuitionStatus.NotActive)
        {
            if (!PlayerRes.HasStatus(IDs.Status.FishersIntuition))
                return;

            IntuitionStatus = IntuitionStatus.Active; // only one try

            if (!extraCfg.Enabled)
                return;
            ExtraCfgGainedIntuition(extraCfg);
        }

        if (IntuitionStatus == IntuitionStatus.Active)
        {
            if (PlayerRes.HasStatus(IDs.Status.FishersIntuition))
                return;

            IntuitionStatus = IntuitionStatus.NotActive; // only one try

            if (!extraCfg.Enabled)
                return;

            ExtraCfgLostIntuition(extraCfg);
        }
    }

    private void ExtraCfgGainedIntuition(ExtraConfig extraCfg)
    {
        // Check if the preset was already swapped
        if (extraCfg.SwapPresetIntuitionGain && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            var preset = Presets.CustomPresets.FirstOrDefault(preset =>
                preset.PresetName == extraCfg.PresetToSwapIntuitionGain);

            _lastStep |= FishingSteps.PresetSwapped;
            if (preset != null)
            {
                Service.Save();
                Presets.SelectedPreset = preset;
                Service.PrintChat(
                    @$"[Extra] Intuition Active - Swapping preset to {extraCfg.PresetToSwapIntuitionGain}");
                Service.Save();
            }
            else
                Service.PrintChat(
                    @$"[Extra] Intuition Active - Preset {extraCfg.PresetToSwapIntuitionGain} not found.");
        }

        // Check if the bait was already swapped
        if (extraCfg.SwapBaitIntuitionGain && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapIntuitionGain);

            _lastStep |= FishingSteps.BaitSwapped; // one try per catch

            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[Extra] Intuition Active - Swapping bait to {extraCfg.BaitToSwapIntuitionGain.Name}");
                Service.Save();
            }
        }
    }

    private void ExtraCfgLostIntuition(ExtraConfig extraCfg)
    {
        // Check if the preset was already swapped
        if (extraCfg.SwapPresetIntuitionLost && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            var preset =
                Presets.CustomPresets.FirstOrDefault(preset =>
                    preset.PresetName == extraCfg.PresetToSwapIntuitionLost);

            _lastStep |= FishingSteps.PresetSwapped;

            if (preset != null)
            {
                Service.Save();
                // one try per catch
                Presets.SelectedPreset = preset;
                Service.PrintChat(@$"[Extra] Intuition Lost - Swapping preset to {extraCfg.PresetToSwapIntuitionLost}");
                Service.Save();
            }
            else
                Service.PrintChat(@$"[Extra] Intuition Lost - Preset {extraCfg.PresetToSwapIntuitionLost} not found.");
        }

        // Check if the bait was already swapped
        if (extraCfg.SwapBaitIntuitionLost && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapIntuitionLost);

            // one try per catch
            _lastStep |= FishingSteps.BaitSwapped;
            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[Extra] Intuition Lost - Swapping bait to {extraCfg.BaitToSwapIntuitionLost.Name}");
                Service.Save();
            }
        }

        if (extraCfg.QuitOnIntuitionLost)
        {
            _lastStep = FishingSteps.Quitting;
        }

        if (extraCfg.StopOnIntuitionLost)
        {
            _lastStep = FishingSteps.None;
        }
    }

    private void CheckAnglersArt(ExtraConfig extraCfg)
    {
        if (!PlayerRes.HasAnglersArtStacks(extraCfg.AnglerStackQtd))
            return;

        if (extraCfg.SwapPresetAnglersArt && !_lastStep.HasFlag(FishingSteps.PresetSwapped))
        {
            var preset =
                Presets.CustomPresets.FirstOrDefault(preset =>
                    preset.PresetName == extraCfg.PresetToSwapAnglersArt);

            _lastStep |= FishingSteps.PresetSwapped;

            if (preset != null)
            {
                Service.Save();
                Presets.SelectedPreset = preset;
                Service.PrintChat(
                    @$"[Extra] Angler's Stack - Swapping preset to {extraCfg.PresetToSwapAnglersArt}");
                Service.Save();
            }
            else
                Service.PrintChat(@$"[Extra] Anglers Stack - Preset {extraCfg.PresetToSwapAnglersArt} not found.");
        }

        if (extraCfg.SwapBaitAnglersArt && !_lastStep.HasFlag(FishingSteps.BaitSwapped))
        {
            var result = Service.BaitManager.ChangeBait(extraCfg.BaitToSwapAnglersArt);
            _lastStep |= FishingSteps.BaitSwapped;
            if (result == BaitManager.ChangeBaitReturn.Success)
            {
                Service.PrintChat(
                    @$"[Extra] Angler's Stack - Swapping bait to {extraCfg.BaitToSwapAnglersArt.Name}");
                Service.Save();
            }
        }
    }
}