using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Text;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Deliveroo.GameData;
using Dalamud.Bindings.ImGui;
using LLib;
using LLib.ImGui;

namespace Deliveroo.Windows;

internal sealed class ConfigWindow : LWindow, IPersistableWindowConfig
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly DeliverooPlugin _plugin;
    private readonly Configuration _configuration;
    private readonly GcRewardsCache _gcRewardsCache;
    private readonly IClientState _clientState;
    private readonly IPluginLog _pluginLog;
    private readonly IconCache _iconCache;
    private readonly GameFunctions _gameFunctions;

    private readonly IReadOnlyDictionary<uint, GcRewardItem> _itemLookup;

    private readonly List<(VirtualKey Key, string Label)> _keyCodes =
    [
        (VirtualKey.NO_KEY, "停用"),
        (VirtualKey.CONTROL, "CTRL"),
        (VirtualKey.SHIFT, "SHIFT"),
        (VirtualKey.MENU, "ALT"),
    ];

    private string _searchString = string.Empty;
    private Configuration.PurchaseOption? _dragDropSource;

    public ConfigWindow(IDalamudPluginInterface pluginInterface, DeliverooPlugin plugin, Configuration configuration,
        GcRewardsCache gcRewardsCache, IClientState clientState, IPluginLog pluginLog, IconCache iconCache,
        GameFunctions gameFunctions)
        : base("Deliveroo－設定###DeliverooConfig")
    {
        _pluginInterface = pluginInterface;
        _plugin = plugin;
        _configuration = configuration;
        _gcRewardsCache = gcRewardsCache;
        _clientState = clientState;
        _pluginLog = pluginLog;
        _iconCache = iconCache;
        _gameFunctions = gameFunctions;

        _itemLookup = _gcRewardsCache.RewardLookup;

        Size = new Vector2(440, 300);
        SizeCondition = ImGuiCond.FirstUseEver;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 300),
            MaximumSize = new Vector2(9999, 9999),
        };
    }

    public WindowConfig WindowConfig => _configuration.ConfigWindowConfig;

    public override void DrawContent()
    {
        if (_configuration.AddVentureIfNoItemToPurchaseSelected())
            Save();

        if (ImGui.BeginTabBar("DeliverooConfigTabs"))
        {
            DrawBuyList();
            DrawCharacterSpecificSettings();
            DrawAdditionalSettings();

            ImGui.EndTabBar();
        }
    }

    private unsafe void DrawBuyList()
    {
        if (ImGui.BeginTabItem("自動購買物品"))
        {
            Configuration.PurchaseOption? itemToRemove = null;
            Configuration.PurchaseOption? itemToAdd = null;
            int indexToAdd = 0;
            if (ImGui.BeginChild("Items", new Vector2(-1, -ImGui.GetFrameHeightWithSpacing()), true,
                    ImGuiWindowFlags.NoSavedSettings))
            {
                for (int i = 0; i < _configuration.ItemsAvailableToPurchase.Count; ++i)
                {
                    var purchaseOption = _configuration.ItemsAvailableToPurchase[i];
                    ImGui.PushID($"###Item{i}");
                    ImGui.BeginDisabled(
                        _configuration.ItemsAvailableToPurchase.Count == 1 && purchaseOption.ItemId == ItemIds.Venture);

                    var item = _itemLookup[purchaseOption.ItemId];
                    using (var icon = _iconCache.GetIcon(item.IconId))
                    {
                        Vector2 pos = ImGui.GetCursorPos();
                        Vector2 iconSize = new Vector2(ImGui.GetTextLineHeight() + ImGui.GetStyle().ItemSpacing.Y);

                        if (icon != null)
                        {
                            ImGui.SetCursorPos(pos + new Vector2(iconSize.X + ImGui.GetStyle().FramePadding.X,
                                ImGui.GetStyle().ItemSpacing.Y / 2));
                        }

                        ImGui.Selectable(
                            $"{item.Name}{(item.Limited ? $" {SeIconChar.Hyadelyn.ToIconString()}" : "")}{(purchaseOption.SameQuantityForAllLists ? $" {((SeIconChar)57412).ToIconString()} (Limit: {purchaseOption.GlobalLimit:N0})" : "")}",
                            false, ImGuiSelectableFlags.SpanAllColumns);

                        if (ImGui.BeginDragDropSource())
                        {
                            ImGui.SetDragDropPayload("DeliverooDragDrop", ReadOnlySpan<byte>.Empty, 0);
                            _dragDropSource = purchaseOption;

                            ImGui.EndDragDropSource();
                        }

                        if (ImGui.BeginDragDropTarget())
                        {
                            if (_dragDropSource != null &&
                                ImGui.AcceptDragDropPayload("DeliverooDragDrop").Handle != null)
                            {
                                itemToAdd = _dragDropSource;
                                indexToAdd = i;

                                _dragDropSource = null;
                            }

                            ImGui.EndDragDropTarget();
                        }

                        ImGui.OpenPopupOnItemClick($"###ctx{i}", ImGuiPopupFlags.MouseButtonRight);
                        if (ImGui.BeginPopup($"###ctx{i}"))
                        {
                            bool sameQuantityForAllLists = purchaseOption.SameQuantityForAllLists;
                            if (ImGui.MenuItem("全域與角色專用清單使用相同數量",
                                    ref sameQuantityForAllLists))
                            {
                                purchaseOption.SameQuantityForAllLists = sameQuantityForAllLists;
                                Save();
                            }

                            if (ImGui.MenuItem($"移除 {_itemLookup[purchaseOption.ItemId].Name}"))
                                itemToRemove = purchaseOption;

                            ImGui.EndPopup();
                        }

                        if (icon != null)
                        {
                            ImGui.SameLine(0, 0);
                            ImGui.SetCursorPos(pos);
                            ImGui.Image(icon.Handle, iconSize);
                        }
                    }

                    ImGui.EndDisabled();
                    ImGui.PopID();
                }
            }

            ImGui.EndChild();

            if (itemToRemove != null)
            {
                _configuration.ItemsAvailableToPurchase.Remove(itemToRemove);
                Save();
            }

            if (itemToAdd != null)
            {
                _configuration.ItemsAvailableToPurchase.Remove(itemToAdd);
                _configuration.ItemsAvailableToPurchase.Insert(indexToAdd, itemToAdd);
                Save();
            }

            List<(uint ItemId, string Name, ushort IconId, bool Limited)> comboValues = _gcRewardsCache.Rewards
                .Where(x => x.SubCategory is RewardSubCategory.Materials or RewardSubCategory.Materiel)
                .Where(x => _configuration.ItemsAvailableToPurchase.All(y => y.ItemId != x.ItemId))
                .Select(x => (x.ItemId, x.Name, x.IconId, x.Limited))
                .OrderBy(x => x.Name)
                .ThenBy(x => x.GetHashCode())
                .ToList();

            if (ImGui.BeginCombo($"##ItemSelection", "新增物品……", ImGuiComboFlags.HeightLarge))
            {
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                bool addFirst = ImGui.InputTextWithHint("", "篩選……", ref _searchString, 256,
                    ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue);

                foreach (var item in comboValues.Where(x =>
                             x.Name.Contains(_searchString, StringComparison.OrdinalIgnoreCase)))
                {
                    using (var icon = _iconCache.GetIcon(item.IconId))
                    {
                        if (icon != null)
                        {
                            ImGui.Image(icon.Handle, new Vector2(ImGui.GetFrameHeight()));
                            ImGui.SameLine();
                            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().FramePadding.X);
                        }
                    }

                    bool addThis =
                        ImGui.Selectable(
                            $"{item.Name}{(item.Limited ? $" {SeIconChar.Hyadelyn.ToIconString()}" : "")}##SelectVenture{item.IconId}");
                    if (addThis || addFirst)
                    {
                        _configuration.ItemsAvailableToPurchase.Add(new Configuration.PurchaseOption
                            { ItemId = item.ItemId });

                        if (addFirst)
                        {
                            addFirst = false;
                            ImGui.CloseCurrentPopup();
                        }

                        Save();
                    }
                }

                ImGui.EndCombo();
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawCharacterSpecificSettings()
    {
        if (ImGui.BeginTabItem("角色設定"))
        {
            if (_clientState is { IsLoggedIn: true, LocalContentId: > 0 })
            {
                string currentCharacterName = _clientState.LocalPlayer!.Name.ToString();
                string currentWorldName = _clientState.LocalPlayer.HomeWorld.Value.Name.ToString();
                ImGui.Text($"目前角色：{currentCharacterName} @ {currentWorldName}");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                var charConfiguration = _plugin.CharacterConfiguration;
                if (charConfiguration != null)
                {
                    bool disableForCharacter = charConfiguration.DisableForCharacter;
                    if (ImGui.Checkbox("對此角色停用插件", ref disableForCharacter))
                    {
                        charConfiguration.DisableForCharacter = disableForCharacter;
                        charConfiguration.Save(_pluginInterface);
                    }

                    ImGui.BeginDisabled(charConfiguration.DisableForCharacter);

                    bool overrideItemsToPurchase = charConfiguration.OverrideItemsToPurchase;
                    if (ImGui.Checkbox("此角色使用自訂購買清單", ref overrideItemsToPurchase))
                    {
                        charConfiguration.OverrideItemsToPurchase = overrideItemsToPurchase;
                        charConfiguration.Save(_pluginInterface);
                    }

                    if (charConfiguration.ItemsToPurchase.Count > 1 ||
                        (charConfiguration.ItemsToPurchase.Count == 1 &&
                         charConfiguration.ItemsToPurchase[0].ItemId != GcRewardItem.None.ItemId))
                    {
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Trash, "清除"))
                        {
                            charConfiguration.ItemsToPurchase.Clear();
                            charConfiguration.Save(_pluginInterface);
                        }
                    }

                    bool useHideArmouryChestItemsFilter = charConfiguration.UseHideArmouryChestItemsFilter;
                    if (ImGui.Checkbox("使用「隱藏兵裝庫物品」篩選器", ref useHideArmouryChestItemsFilter))
                    {
                        charConfiguration.UseHideArmouryChestItemsFilter = useHideArmouryChestItemsFilter;
                        charConfiguration.Save(_pluginInterface);
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker(
                        "The default filter for all characters is 'Hide Gear Set Items', but you may want to override this to hide all Armoury Chest items (regardless of whether they're part of a gear set) e.g. for your main character.");

                    bool ignoreMinimumSealsToKeep = charConfiguration.IgnoreMinimumSealsToKeep;
                    if (ImGui.Checkbox("忽略「保留的最低軍票數量」設定", ref ignoreMinimumSealsToKeep))
                    {
                        charConfiguration.IgnoreMinimumSealsToKeep = ignoreMinimumSealsToKeep;
                        charConfiguration.Save(_pluginInterface);
                    }

                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker(
                        "When enabled, all GC seals will be spent. This is effectively the same as setting 'Minimum Seals to keep' to 0.");

                    ImGui.EndDisabled();
                    ImGui.Spacing();
                    ImGui.Separator();
                    ImGui.Spacing();

                    ImGui.BeginDisabled(!ImGui.GetIO().KeyCtrl);
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PersonCircleMinus,
                            "移除角色專用設定"))
                    {
                        charConfiguration.Delete(_pluginInterface);
                        _plugin.CharacterConfiguration = null;
                    }

                    ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && !ImGui.GetIO().KeyCtrl)
                        ImGui.SetTooltip(
                            $"Hold CTRL to remove the configuration for {currentCharacterName} (non-reversible).");
                }
                else
                {
                    // no settings
                    if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PersonCirclePlus,
                            "啟用角色專用設定"))
                    {
                        _plugin.CharacterConfiguration = new()
                        {
                            LocalContentId = _clientState.LocalContentId,
                            CachedPlayerName = currentCharacterName,
                            CachedWorldName = currentWorldName,
                        };
                        _plugin.CharacterConfiguration.Save(_pluginInterface);
                        _pluginLog.Information(
                            $"Created character-specific configuration for {_clientState.LocalContentId}");
                    }
                }
            }
            else
            {
                ImGui.Text("目前尚未登入。");
            }

            ImGui.EndTabItem();
        }
    }

    private void DrawAdditionalSettings()
    {
        if (ImGui.BeginTabItem("其他設定"))
        {
            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            int reservedSealCount = _configuration.ReservedSealCount;
            if (ImGui.InputInt("保留的最低軍票數量（例如分隊任務）", ref reservedSealCount, 1000))
            {
                _configuration.ReservedSealCount =
                    Math.Max(0, Math.Min((int)_gameFunctions.MaxSealCap, reservedSealCount));
                Save();
            }

            ImGui.BeginDisabled(reservedSealCount <= 0);
            bool reserveDifferentSealCountAtMaxRank = _configuration.ReserveDifferentSealCountAtMaxRank;
            if (ImGui.Checkbox("最高軍階時使用不同數量", ref reserveDifferentSealCountAtMaxRank))
            {
                _configuration.ReserveDifferentSealCountAtMaxRank = reserveDifferentSealCountAtMaxRank;
                Save();
            }

            if (reserveDifferentSealCountAtMaxRank)
            {
                float indentSize = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X;
                ImGui.Indent(indentSize);
                ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 100);
                int reservedSealCountAtMaxRank = _configuration.ReservedSealCountAtMaxRank;
                if (ImGui.InputInt("最高軍階時保留的最低軍票數量", ref reservedSealCountAtMaxRank))
                {
                    _configuration.ReservedSealCountAtMaxRank = Math.Max(0,
                        Math.Min((int)_gameFunctions.MaxSealCap, reservedSealCountAtMaxRank));
                    Save();
                }

                ImGui.Unindent(indentSize);
            }

            ImGui.EndDisabled();

            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            int selectedKey = Math.Max(0, _keyCodes.FindIndex(x => x.Key == _configuration.QuickTurnInKey));
            if (ImGui.Combo("快速繳納按鍵", ref selectedKey, _keyCodes.Select(x => x.Label).ToArray(),
                    _keyCodes.Count))
            {
                _configuration.QuickTurnInKey = _keyCodes[selectedKey].Key;
                Save();
            }

            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            var xivChatTypes = Enum.GetValues<XivChatType>()
                .Where(x => x != XivChatType.StandardEmote)
                .ToArray();
            var selectedChatType = Array.IndexOf(xivChatTypes, _configuration.ChatType);
            string[] chatTypeNames = xivChatTypes
                .Select(t => t.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? t.ToString())
                .ToArray();
            if (ImGui.Combo("狀態更新的聊天頻道", ref selectedChatType, chatTypeNames,
                    chatTypeNames.Length))
            {
                _configuration.ChatType = xivChatTypes[selectedChatType];
                Save();
            }

            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            List<(int Rank, string Name)> rankUpComboValues = Enumerable.Range(1, 30)
                .Select(x => x == 1 ? (0, "---") : (x, $"Rank {x}"))
                .ToList();
            int pauseAtRank = Math.Max(rankUpComboValues.FindIndex(x => x.Rank == _configuration.PauseAtRank), 0);
            if (ImGui.Combo("達到指定部隊階級時暫停", ref pauseAtRank,
                    rankUpComboValues.Select(x => x.Name).ToArray(),
                    rankUpComboValues.Count))
            {
                _configuration.PauseAtRank = rankUpComboValues[pauseAtRank].Rank;
                Save();
            }

            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 120);
            string[] behaviorOnOtherWorldNames = { "---", "顯示警告", "停用繳納" };
            int behaviorOnOtherWorld = (int)_configuration.BehaviorOnOtherWorld;
            if (ImGui.Combo("不在所屬世界時的行為", ref behaviorOnOtherWorld,
                    behaviorOnOtherWorldNames, behaviorOnOtherWorldNames.Length))
            {
                _configuration.BehaviorOnOtherWorld = (Configuration.EBehaviorOnOtherWorld)behaviorOnOtherWorld;
                Save();
            }

            ImGui.Separator();

            bool disableFrameLimiter = _configuration.DisableFrameLimiter;
            if (ImGui.Checkbox("停用遊戲設定「遊戲視窗未啟用時限制幀數」",
                    ref disableFrameLimiter))
            {
                _configuration.DisableFrameLimiter = disableFrameLimiter;
                Save();
            }

            ImGui.Indent();
            ImGui.BeginDisabled(!disableFrameLimiter);
            bool uncapFrameRate = _configuration.UncapFrameRate;
            if (ImGui.Checkbox("將幀數設為無上限", ref uncapFrameRate))
            {
                _configuration.UncapFrameRate = uncapFrameRate;
                Save();
            }

            ImGui.EndDisabled();
            ImGui.Unindent();
            ImGui.EndTabItem();
        }
    }


    private void Save() => _pluginInterface.SavePluginConfig(_configuration);

    public void SaveWindowConfig() => Save();
}
