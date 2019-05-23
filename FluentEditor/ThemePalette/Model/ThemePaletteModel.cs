﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using FluentEditor.ThemePalette.Data;
using FluentEditorShared;
using FluentEditorShared.ColorPalette;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Storage;
using Windows.UI;

namespace FluentEditor.ThemePalette.Model
{
    public interface IThemePaletteModel
    {
        Task InitializeData(IStringProvider stringProvider, string dataPath);
        Task HandleAppSuspend();

        void AddOrReplacePreset(ThemePreset preset);
        void ApplyPreset(ThemePreset preset);
        ObservableList<ThemePreset> Presets { get; }
        ThemePreset ActivePreset { get; }
        event Action<IThemePaletteModel> ActivePresetChanged;

        IReadOnlyList<ThemeColorMapping> LightColorMapping { get; }
        IReadOnlyList<ThemeColorMapping> DarkColorMapping { get; }
        ColorPaletteEntry LightRegion { get; }
        ColorPaletteEntry DarkRegion { get; }
        ColorPalette LightBase { get; }
        ColorPalette DarkBase { get; }
        ColorPalette LightPrimary { get; }
        ColorPalette DarkPrimary { get; }
    }

    public class ThemePaletteModel : IThemePaletteModel
    {
        public async Task InitializeData(IStringProvider stringProvider, string dataPath)
        {
            _stringProvider = stringProvider;

            StorageFile file = await StorageFile.GetFileFromApplicationUriAsync(new Uri(dataPath));
            string dataString = await FileIO.ReadTextAsync(file);
            JsonObject rootObject = JsonObject.Parse(dataString);

            _whiteColor = new ColorPaletteEntry(Colors.White, _stringProvider.GetString("DarkThemeTextContrastTitle"), null, FluentEditorShared.Utils.ColorStringFormat.PoundRGB, null);
            _blackColor = new ColorPaletteEntry(Colors.Black, _stringProvider.GetString("LightThemeTextContrastTitle"), null, FluentEditorShared.Utils.ColorStringFormat.PoundRGB, null);

            var lightRegionNode = rootObject[nameof(LightRegion)].GetObject();
            _lightRegion = ColorPaletteEntry.Parse(lightRegionNode, null);

            var darkRegionNode = rootObject[nameof(DarkRegion)].GetObject();
            _darkRegion = ColorPaletteEntry.Parse(darkRegionNode, null);

            var lightBaseNode = rootObject[nameof(LightBase)].GetObject();
            _lightBase = ThemeColorPalette.Parse(lightBaseNode, null);

            var darkBaseNode = rootObject[nameof(DarkBase)].GetObject();
            _darkBase = ThemeColorPalette.Parse(darkBaseNode, null);

            var lightPrimaryNode = rootObject[nameof(LightPrimary)].GetObject();
            _lightPrimary = ThemeColorPalette.Parse(lightPrimaryNode, null);

            var darkPrimaryNode = rootObject[nameof(DarkPrimary)].GetObject();
            _darkPrimary = ThemeColorPalette.Parse(darkPrimaryNode, null);

            _presets = new ObservableList<ThemePreset>();
            if (rootObject.ContainsKey("Presets"))
            {
                var presetsNode = rootObject["Presets"].GetArray();
                foreach (var presetNode in presetsNode)
                {
                    _presets.Add(ThemePreset.Parse(presetNode.GetObject()));
                }
            }
            if (_presets.Count >= 1)
            {
                ApplyPreset(_presets[0]);
            }

            UpdateActivePreset();

            _lightRegion.ContrastColors = new List<ContrastColorWrapper>() { new ContrastColorWrapper(_whiteColor, false, false), new ContrastColorWrapper(_blackColor, true, true), new ContrastColorWrapper(_lightBase.BaseColor, true, false), new ContrastColorWrapper(_lightPrimary.BaseColor, true, false) };
            _darkRegion.ContrastColors = new List<ContrastColorWrapper>() { new ContrastColorWrapper(_whiteColor, true, true), new ContrastColorWrapper(_blackColor, false, false), new ContrastColorWrapper(_darkBase.BaseColor, true, false), new ContrastColorWrapper(_darkPrimary.BaseColor, true, false) };
            _lightBase.ContrastColors = new List<ContrastColorWrapper>() { new ContrastColorWrapper(_whiteColor, false, false), new ContrastColorWrapper(_blackColor, true, true), new ContrastColorWrapper(_lightRegion, true, false), new ContrastColorWrapper(_lightPrimary.BaseColor, true, false) };
            _darkBase.ContrastColors = new List<ContrastColorWrapper>() { new ContrastColorWrapper(_whiteColor, true, true), new ContrastColorWrapper(_blackColor, false, false), new ContrastColorWrapper(_darkRegion, true, false), new ContrastColorWrapper(_darkPrimary.BaseColor, true, false) };
            _lightPrimary.ContrastColors = new List<ContrastColorWrapper>() { new ContrastColorWrapper(_whiteColor, true, true), new ContrastColorWrapper(_blackColor, false, false), new ContrastColorWrapper(_lightRegion, true, false), new ContrastColorWrapper(_lightBase.BaseColor, true, false) };
            _darkPrimary.ContrastColors = new List<ContrastColorWrapper>() { new ContrastColorWrapper(_whiteColor, true, true), new ContrastColorWrapper(_blackColor, false, false), new ContrastColorWrapper(_darkRegion, true, false), new ContrastColorWrapper(_darkBase.BaseColor, true, false) };

            _lightColorMappings = ThemeColorMapping.ParseList(rootObject["LightPaletteMapping"].GetArray(), _lightRegion, _darkRegion, _lightBase, _darkBase, _lightPrimary, _darkPrimary, _whiteColor, _blackColor);
            _lightColorMappings.Sort((a, b) =>
            {
                return a.Target.ToString().CompareTo(b.Target.ToString());
            });

            _darkColorMappings = ThemeColorMapping.ParseList(rootObject["DarkPaletteMapping"].GetArray(), _lightRegion, _darkRegion, _lightBase, _darkBase, _lightPrimary, _darkPrimary, _whiteColor, _blackColor);
            _darkColorMappings.Sort((a, b) =>
            {
                return a.Target.ToString().CompareTo(b.Target.ToString());
            });

            _lightRegion.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            _darkRegion.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            _lightBase.BaseColor.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            _darkBase.BaseColor.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            _lightPrimary.BaseColor.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            _darkPrimary.BaseColor.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            foreach (var entry in _lightBase.Palette)
            {
                entry.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            }
            foreach (var entry in _darkBase.Palette)
            {
                entry.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            }
            foreach (var entry in _lightPrimary.Palette)
            {
                entry.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            }
            foreach (var entry in _darkPrimary.Palette)
            {
                entry.ActiveColorChanged += PaletteEntry_ActiveColorChanged;
            }

            if (_lightRegion.Description == null)
            {
                _lightRegion.Description = GenerateMappingDescription(_lightRegion, _lightColorMappings);
            }
            if (_darkRegion.Description == null)
            {
                _darkRegion.Description = GenerateMappingDescription(_darkRegion, _darkColorMappings);
            }
            foreach (var entry in _lightBase.Palette)
            {
                if (entry.Description == null)
                {
                    entry.Description = GenerateMappingDescription(entry, _lightColorMappings);
                }
            }
            foreach (var entry in _darkBase.Palette)
            {
                if (entry.Description == null)
                {
                    entry.Description = GenerateMappingDescription(entry, _darkColorMappings);
                }
            }
            foreach (var entry in _lightPrimary.Palette)
            {
                if (entry.Description == null)
                {
                    entry.Description = GenerateMappingDescription(entry, _lightColorMappings);
                }
            }
            foreach (var entry in _darkPrimary.Palette)
            {
                if (entry.Description == null)
                {
                    entry.Description = GenerateMappingDescription(entry, _darkColorMappings);
                }
            }
        }

        private string GenerateMappingDescription(IColorPaletteEntry paletteEntry, List<ThemeColorMapping> mappings)
        {
            string retVal = string.Empty;

            foreach (var mapping in mappings)
            {
                if (mapping.Source == paletteEntry)
                {
                    if (retVal != string.Empty)
                    {
                        retVal += ", ";
                    }
                    retVal += mapping.Target.ToString();
                }
            }

            if (retVal != string.Empty)
            {
                return string.Format(_stringProvider.GetString("ColorFlyoutMappingDescription"), retVal);
            }
            else
            {
                return null;
            }
        }

        public Task HandleAppSuspend()
        {
            // Currently nothing to do here
            return Task.CompletedTask;
        }

        private void PaletteEntry_ActiveColorChanged(IColorPaletteEntry obj)
        {
            UpdateActivePreset();
        }

        private void UpdateActivePreset()
        {
            if (_presets != null)
            {
                for (int i = 0; i < _presets.Count; i++)
                {
                    if (_presets[i].IsPresetActive(this))
                    {
                        ActivePreset = _presets[i];
                        return;
                    }
                }
            }
            ActivePreset = null;
        }

        private IStringProvider _stringProvider;

        public void AddOrReplacePreset(ThemePreset preset)
        {
            if (!string.IsNullOrEmpty(preset.Name))
            {
                var oldPreset = _presets.FirstOrDefault<ThemePreset>((a) => a.Id == preset.Id);
                if (oldPreset != null)
                {
                    _presets.Remove(oldPreset);
                }
            }

            _presets.Add(preset);

            UpdateActivePreset();
        }

        public void ApplyPreset(ThemePreset preset)
        {
            if (preset == null)
            {
                ActivePreset = null;
                return;
            }

            _lightRegion.ActiveColor = preset.LightRegionColor;
            _darkRegion.ActiveColor = preset.DarkRegionColor;
            _lightBase.BaseColor.ActiveColor = preset.LightBaseColor;
            _darkBase.BaseColor.ActiveColor = preset.DarkBaseColor;
            _lightPrimary.BaseColor.ActiveColor = preset.LightPrimaryColor;
            _darkPrimary.BaseColor.ActiveColor = preset.DarkPrimaryColor;

            ApplyPresetOverrides(_lightBase.Palette, preset.LightBaseOverrides);
            ApplyPresetOverrides(_darkBase.Palette, preset.DarkBaseOverrides);
            ApplyPresetOverrides(_lightPrimary.Palette, preset.LightPrimaryOverrides);
            ApplyPresetOverrides(_darkPrimary.Palette, preset.DarkPrimaryOverrides);
        }

        private void ApplyPresetOverrides(IReadOnlyList<EditableColorPaletteEntry> palette, Dictionary<int, Color> overrides)
        {
            for (int i = 0; i < palette.Count; i++)
            {
                if (overrides != null && overrides.ContainsKey(i))
                {
                    palette[i].CustomColor = overrides[i];
                    palette[i].UseCustomColor = true;
                }
                else
                {
                    palette[i].UseCustomColor = false;
                }
            }
        }

        private ObservableList<ThemePreset> _presets;
        public ObservableList<ThemePreset> Presets
        {
            get { return _presets; }
        }

        private ThemePreset _activePreset;
        public ThemePreset ActivePreset
        {
            get { return _activePreset; }
            private set
            {
                if (_activePreset != value)
                {
                    _activePreset = value;
                    ActivePresetChanged?.Invoke(this);
                }
            }
        }

        public event Action<IThemePaletteModel> ActivePresetChanged;

        private List<ThemeColorMapping> _lightColorMappings;
        public IReadOnlyList<ThemeColorMapping> LightColorMapping
        {
            get { return _lightColorMappings; }
        }

        private List<ThemeColorMapping> _darkColorMappings;
        public IReadOnlyList<ThemeColorMapping> DarkColorMapping
        {
            get { return _darkColorMappings; }
        }

        private ColorPaletteEntry _whiteColor;
        private ColorPaletteEntry _blackColor;

        private ColorPaletteEntry _lightRegion;
        public ColorPaletteEntry LightRegion
        {
            get { return _lightRegion; }
        }

        private ColorPaletteEntry _darkRegion;
        public ColorPaletteEntry DarkRegion
        {
            get { return _darkRegion; }
        }

        private ColorPalette _lightBase;
        public ColorPalette LightBase
        {
            get { return _lightBase; }
        }

        private ColorPalette _darkBase;
        public ColorPalette DarkBase
        {
            get { return _darkBase; }
        }

        private ColorPalette _lightPrimary;
        public ColorPalette LightPrimary
        {
            get { return _lightPrimary; }
        }

        private ColorPalette _darkPrimary;
        public ColorPalette DarkPrimary
        {
            get { return _darkPrimary; }
        }
    }
}
