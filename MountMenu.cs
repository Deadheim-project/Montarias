using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValheimMontarias.Prefabs;
using ValheimMontarias.UI;

namespace ValheimMontarias
{
    /// <summary>
    /// The mount menu (U), built as the same window NpcValheim uses for its NPCs: the wooden
    /// frame, the title bar you drag it by, the tab strip, the inlays and the yellow status line,
    /// at the same sizes, so it reads as part of the same game.
    ///
    /// Tabs: Montarias (the mount journal: everything that exists, what you can summon, what
    /// you are missing), Habilidade (the riding skill levels) and, for admins, Admin. What you
    /// own and know is the server's record (RiderClient); the menu redraws when it changes.
    /// </summary>
    internal sealed class MountMenu : MonoBehaviour
    {
        private enum Tab { Journal, Skill, Admin }

        private const float Width = 940f;
        private const float Height = 640f;
        private const string SkillIcon = "SaddleLox";

        private static MountMenu _instance;
        private static MountProfile _selectedMount;

        private GameObject _canvas;
        private TextMeshProUGUI _status;
        private RectTransform _tabStrip;
        private RectTransform _content;
        private readonly List<(Button button, Tab tab)> _tabs = new List<(Button, Tab)>();

        private RectTransform _journalRoot;
        private RectTransform _journalList;
        private RectTransform _journalDetail;
        private RectTransform _skillRoot;
        private RectTransform _skillList;
        private TextMeshProUGUI _skillSummary;
        private RectTransform _adminRoot;
        private RectTransform _adminList;
        private RectTransform _adminEditor;

        private readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();
        private bool _open;
        private Tab _tab = Tab.Journal;
        private MountProfile _focus;
        private MountProfile _adminProfile;
        private int _seenRevision = -1;
        private int _seenMessage;

        public static bool IsOpen => _instance != null && _instance._open;
        public static bool AdminTabOpen => IsOpen && _instance._tab == Tab.Admin;

        public static void EnsureCreated()
        {
            if (_instance != null) return;
            var go = new GameObject("ValheimMontarias_Menu");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<MountMenu>();
        }

        public static void Toggle()
        {
            if (_instance == null) return;
            if (_instance._open) _instance.CloseInternal();
            else _instance.OpenInternal(Tab.Journal);
        }

        public static void Open() => _instance?.OpenInternal(Tab.Journal);

        public static void OpenAdmin(MountProfile profile = null) =>
            _instance?.OpenInternal(Tab.Admin, profile);

        public static void Close() => _instance?.CloseInternal();

        /// <summary>The selected mount if usable, otherwise the first usable mount.</summary>
        public static MountProfile Selected()
        {
            var chosen = _selectedMount;
            if (MountRoster.Usable(chosen)) return chosen;
            var usable = MountRoster.UsableMounts();
            if (usable.Count > 0) return usable[0];
            if (chosen != null) return chosen;
            var all = MountSettings.All;
            return all != null && all.Length > 0 ? all[0] : null;
        }

        public static void Select(MountProfile profile)
        {
            if (profile == null) return;
            _selectedMount = profile;
            _instance?.Redraw();
        }

        // ---- lifetime ----

        private void Update()
        {
            if (Player.m_localPlayer == null)
            {
                if (_open) CloseInternal();
                return;
            }

            if (!Typing() && !Console.IsVisible())
            {
                if (MountSettings.MenuKey != null && MountSettings.MenuKey.Value.IsDown())
                    Toggle();

            }

            if (!_open) return;
            if (Player.m_localPlayer.IsDead() || Console.IsVisible())
            {
                CloseInternal();
                return;
            }

            if (_seenRevision != RiderClient.Revision)
            {
                _seenRevision = RiderClient.Revision;
                Redraw();
            }
            if (_seenMessage != RiderClient.MessageRevision)
            {
                _seenMessage = RiderClient.MessageRevision;
                Say(RiderClient.LastMessage);
            }

            if (!Typing() && Input.GetKeyDown(KeyCode.Escape))
                CloseInternal();
        }

        private void LateUpdate()
        {
            if (!_open) return;
            // Through ZCursor, the game's own wrapper, so its idea of the cursor stays true.
            if (ZCursor.LockState != CursorLockMode.None) ZCursor.LockState = CursorLockMode.None;
            if (!ZCursor.IsVisible) ZCursor.Show();
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void OpenInternal(Tab tab, MountProfile adminProfile = null)
        {
            // Only when opening: once open, Menu.IsVisible answers yes for this very window.
            if (Player.m_localPlayer == null || (!_open && BusyElsewhere())) return;
            if (tab == Tab.Admin && !Access.IsAdmin())
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Apenas administradores.");
                if (!_open) return;
                tab = Tab.Journal;
            }

            if (!_open)
            {
                if (!ValheimUi.EnsureAssets())
                {
                    Plugin.Log.LogWarning("ValheimMontarias: UI do jogo ainda não carregou, menu não abriu");
                    return;
                }

                _canvas = ValheimUi.CreateCanvas("ValheimMontarias_Window", 4900);
                if (_canvas == null)
                {
                    Plugin.Log.LogWarning("ValheimMontarias: GUI root não encontrado, menu não abriu");
                    return;
                }

                BuildWindow();
                _open = true;
                _seenMessage = RiderClient.MessageRevision;
                RiderClient.Request(true);
            }

            _adminProfile = adminProfile ?? _adminProfile ?? MountSettings.Default;
            _focus ??= Selected();
            SetTab(tab);
        }

        private void CloseInternal()
        {
            if (!_open) return;
            _open = false;
            _tab = Tab.Journal;
            _tabs.Clear();
            _journalRoot = _journalList = _journalDetail = null;
            _skillRoot = _skillList = null;
            _skillSummary = null;
            _adminRoot = _adminList = _adminEditor = null;
            _status = null;
            if (_canvas != null) Destroy(_canvas);
            _canvas = null;

            if (AdminMenu.IsOpen || Menu.IsVisible() || (InventoryGui.instance != null && InventoryGui.IsVisible())) return;
            ZCursor.LockState = CursorLockMode.Locked;
            ZCursor.Hide();
        }

        // ---- the window (NpcValheim's NpcWindow, measure for measure) ----

        private void BuildWindow()
        {
            var panel = ValheimUi.CreatePanel(_canvas.transform, Width, Height);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;

            var titleBar = ValheimUi.CreateRect("TitleBar", panel);
            ValheimUi.Anchor(titleBar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -58f), Vector2.zero);
            titleBar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            titleBar.gameObject.AddComponent<DragWindow>().Target = panel;

            var title = ValheimUi.CreateLabel(titleBar, "Montarias", 30, ValheimUi.Orange,
                TextAlignmentOptions.Center, display: true);
            ValheimUi.Stretch((RectTransform)title.transform, 60f, 10f);

            var close = ValheimUi.CreateButton(panel, "X", 36f, 36f, 18);
            ValheimUi.Anchor((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-52f, -52f), new Vector2(-16f, -16f));
            close.onClick.AddListener(CloseInternal);

            _tabStrip = ValheimUi.CreateRect("Tabs", panel);
            ValheimUi.Anchor(_tabStrip, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -104f), new Vector2(-24f, -60f));
            var strip = _tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 8f;
            strip.childControlWidth = true;
            strip.childControlHeight = true;
            strip.childForceExpandWidth = false;
            strip.childAlignment = TextAnchor.MiddleLeft;

            _content = ValheimUi.CreateRect("Content", panel);
            ValheimUi.Anchor(_content, Vector2.zero, Vector2.one,
                new Vector2(24f, 52f), new Vector2(-24f, -110f));

            _status = ValheimUi.CreateLabel(panel, HintText(), 15, ValheimUi.Yellow, TextAlignmentOptions.Left);
            ValheimUi.Anchor((RectTransform)_status.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 14f), new Vector2(-26f, 44f));

            _tabs.Clear();
            AddTab("Montarias", Tab.Journal);
            AddTab("Habilidade", Tab.Skill);
            if (Access.IsAdmin())
                AddTab("Admin", Tab.Admin);

            _journalRoot = BuildJournal(_content);
            _skillRoot = BuildSkill(_content);
            _adminRoot = BuildAdmin(_content);
        }

        private void AddTab(string label, Tab tab)
        {
            var button = ValheimUi.CreateButton(_tabStrip, label, 150f, 40f, 16);
            button.onClick.AddListener(() => SetTab(tab));
            _tabs.Add((button, tab));
        }

        private void SetTab(Tab tab)
        {
            if (tab == Tab.Admin && !Access.IsAdmin())
            {
                Say("Apenas administradores.");
                tab = Tab.Journal;
            }

            _tab = tab;
            if (_journalRoot != null) _journalRoot.gameObject.SetActive(tab == Tab.Journal);
            if (_skillRoot != null) _skillRoot.gameObject.SetActive(tab == Tab.Skill);
            if (_adminRoot != null) _adminRoot.gameObject.SetActive(tab == Tab.Admin);

            // Valheim keeps the active tab lit and dims the others.
            foreach (var (button, which) in _tabs)
            {
                bool on = which == tab;
                var text = button.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null) text.color = on ? ValheimUi.Yellow : ValheimUi.Orange;
                if (button.image != null)
                    button.image.color = on ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);
            }

            Redraw();
            Say(HintText());
        }

        private void Redraw()
        {
            if (!_open) return;
            if (_tab == Tab.Journal) RebuildJournal();
            else if (_tab == Tab.Skill) RebuildSkill();
            else RebuildAdmin();
        }

        // ---- Montarias: the journal ----

        private RectTransform BuildJournal(Transform parent)
        {
            var root = ValheimUi.CreateRect("Journal", parent);
            ValheimUi.Stretch(root, 0f, 0f);

            var left = ValheimUi.CreateInlay(root, "List");
            ValheimUi.Anchor(left, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(320f, 0f));
            var area = ValheimUi.CreateRect("Area", left);
            ValheimUi.Anchor(area, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            _journalList = ValheimUi.CreateScrollList(area, spacing: 4f);

            _journalDetail = ValheimUi.CreateInlay(root, "Detail");
            ValheimUi.Anchor(_journalDetail, Vector2.zero, Vector2.one, new Vector2(332f, 0f), Vector2.zero);
            return root;
        }

        private void RebuildJournal()
        {
            ClearChildren(_journalList);
            var all = MountSettings.All;
            if (all == null || _journalList == null) return;

            var selected = Selected();
            if (_focus == null) _focus = selected;
            foreach (var profile in all)
            {
                if (profile == null) continue;
                var row = ValheimUi.CreateButton(_journalList, "", 0f, 64f, 16);
                row.onClick.AddListener(() =>
                {
                    _focus = profile;
                    RebuildJournal();
                });
                if (row.image != null)
                    row.image.color = profile == _focus ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);

                var icon = ValheimUi.CreateRect("Icon", row.transform);
                ValheimUi.Anchor(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(8f, -24f), new Vector2(56f, 24f));
                var image = icon.gameObject.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.sprite = IconOf(profile);
                image.enabled = image.sprite != null;
                if (!MountRoster.Usable(profile)) image.color = new Color(0.55f, 0.55f, 0.55f, 1f);

                var label = row.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    ValheimUi.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one,
                        new Vector2(64f, 4f), new Vector2(-8f, -4f));
                    label.alignment = TextAlignmentOptions.Left;
                    label.color = ValheimUi.Beige;
                    label.text = $"{profile.Name}\n<size=12><color=#9a9188>{StateOf(profile, selected)}</color></size>";
                }
            }

            RebuildDetail(selected);
        }

        private void RebuildDetail(MountProfile selected)
        {
            ClearChildren(_journalDetail);
            var profile = _focus;
            if (profile == null || _journalDetail == null) return;

            var art = ValheimUi.CreateRect("Art", _journalDetail);
            ValheimUi.Anchor(art, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -176f), new Vector2(176f, -16f));
            var image = art.gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.sprite = IconOf(profile);
            image.enabled = image.sprite != null;

            var name = ValheimUi.CreateLabel(_journalDetail, profile.Name, 26, ValheimUi.Orange,
                TextAlignmentOptions.TopLeft, display: true);
            ValheimUi.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(192f, -56f), new Vector2(-16f, -16f));

            var state = ValheimUi.CreateLabel(_journalDetail, StateOf(profile, selected), 16, ValheimUi.Yellow,
                TextAlignmentOptions.TopLeft);
            ValheimUi.Anchor((RectTransform)state.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(192f, -84f), new Vector2(-16f, -58f));

            float bonus = MountRoster.SpeedBonus;
            var stats = ValheimUi.CreateLabel(_journalDetail,
                $"Habilidade exigida: {RidingRanks.NameOf(profile.MinRank)}\n" +
                (string.IsNullOrEmpty(profile.Boss) ? "" : $"Passe exigido: {MountRoster.BossName(profile)}\n") +
                (profile.Flies ? "Voa: Espaço decola e sobe, Ctrl desce e pousa.\n" : "") +
                $"Velocidade: {Value(profile.RunSpeed) * bonus:0.#}" +
                (bonus > 1.001f ? $" <color=#9a9188>(+{(bonus - 1f) * 100f:0}% da sua habilidade)</color>" : "") + "\n" +
                $"Conjuração: {Mathf.Max(0.1f, Value(profile.CastSeconds) * MountRoster.CastMultiplier):0.#}s" +
                (MountRoster.CastMultiplier < 0.999f ? $" <color=#9a9188>(-{(1f - MountRoster.CastMultiplier) * 100f:0}% da sua habilidade)</color>" : "") + "\n" +
                "Use o item da montaria para montar; o item ou H para descer.",
                15, ValheimUi.Beige, TextAlignmentOptions.TopLeft);
            ValheimUi.Anchor((RectTransform)stats.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(192f, -200f), new Vector2(-16f, -92f));

            // The mount item is how you mount from the hotbar; whoever owns the mount can take
            // another one here if theirs was lost. It does nothing for anyone who does not own it.
            if (MountRoster.Owns(profile) && !WhistleItem.Has(Player.m_localPlayer, profile))
            {
                var take = ValheimUi.CreateButton(_journalDetail, "Pegar o item", 150f, 40f, 16);
                ValheimUi.Anchor((RectTransform)take.transform, new Vector2(0f, 0f), new Vector2(0f, 0f),
                    new Vector2(16f, 16f), new Vector2(166f, 56f));
                take.onClick.AddListener(() =>
                {
                    if (WhistleItem.GiveTo(Player.m_localPlayer, profile))
                        Say("Item da montaria no inventário: use-o para montar.");
                    RebuildJournal();
                });
            }

            string blocker = MountRoster.Blocker(profile);
            if (blocker != null)
            {
                var hint = ValheimUi.CreateLabel(_journalDetail, blocker, 15, ValheimUi.Muted, TextAlignmentOptions.TopLeft);
                ValheimUi.Anchor((RectTransform)hint.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(16f, 64f), new Vector2(-16f, 120f));
                return;
            }

            var summon = ValheimUi.CreateButton(_journalDetail, IsSummoned(profile) ? "Desmontar" : "Montar", 150f, 40f, 16);
            ValheimUi.Anchor((RectTransform)summon.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-166f, 16f), new Vector2(-16f, 56f));
            summon.onClick.AddListener(() =>
            {
                CloseInternal();
                MountControl.TrySummonProfile(Player.m_localPlayer, profile);
            });

            bool isSelected = profile == selected;
            var choose = ValheimUi.CreateButton(_journalDetail,
                isSelected ? "Selecionada" : "Selecionar", 170f, 40f, 16);
            ValheimUi.Anchor((RectTransform)choose.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-344f, 16f), new Vector2(-174f, 56f));
            choose.interactable = !isSelected;
            choose.onClick.AddListener(() =>
            {
                Select(profile);
                Say($"{profile.Name} selecionada.");
            });

            // Aesir 28/30: cosmetic controls (name and skin) -- shown only when the feature
            // is active and the player owns the mount. VIP check is done server-side; we show
            // the controls to everyone so non-VIP gets a clear server message explaining why.
            if (MountRoster.Owns(profile) &&
                ((MountSettings.MountNameActive?.Value ?? false) || (MountSettings.MountSkinActive?.Value ?? false)))
            {
                AddCosmeticSection(_journalDetail, profile);
            }
        }

        private void AddCosmeticSection(RectTransform parent, MountProfile profile)
        {
            // Separator label
            var sep = ValheimUi.CreateLabel(parent, "— Cosméticos (Aesir) —", 14,
                ValheimUi.Orange, TextAlignmentOptions.Center, display: true);
            ValheimUi.Anchor((RectTransform)sep.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(16f, -230f), new Vector2(-16f, -210f));

            // --- Nome (Aesir 28) ---
            if (MountSettings.MountNameActive?.Value ?? false)
            {
                var nameLabel = ValheimUi.CreateLabel(parent,
                    $"Nome: {(string.IsNullOrEmpty(RiderClient.NameOf(profile.Id)) ? "<cor padrão>" : RiderClient.NameOf(profile.Id))}",
                    13, ValheimUi.Beige, TextAlignmentOptions.Left);
                ValheimUi.Anchor((RectTransform)nameLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(16f, -258f), new Vector2(-16f, -234f));

                var nameRow = ValheimUi.CreateRect("NomeRow", parent);
                ValheimUi.Anchor(nameRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                    new Vector2(16f, -298f), new Vector2(-16f, -262f));
                var nameLayout = nameRow.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                nameLayout.spacing = 6f;
                nameLayout.childControlWidth = true;
                nameLayout.childControlHeight = true;
                nameLayout.childForceExpandWidth = false;
                nameLayout.childForceExpandHeight = true;
                nameLayout.childAlignment = TextAnchor.MiddleLeft;

                int maxLen = MountSettings.MountNameMaxLength?.Value ?? 20;
                var nameField = ValheimUi.CreateInputField(nameRow, RiderClient.NameOf(profile.Id), 200f, 34f);
                nameField.characterLimit = maxLen;
                var nameFlexEl = nameField.gameObject.GetComponent<UnityEngine.UI.LayoutElement>()
                    ?? nameField.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                nameFlexEl.flexibleWidth = 1f;

                var nameOk = ValheimUi.CreateButton(nameRow, "OK", 60f, 34f, 13);
                nameOk.onClick.AddListener(() =>
                {
                    string argument = profile.Id + "\n" + nameField.text;
                    if (RiderNet.Send(RiderNet.ActionSetName, argument))
                        Say("Pedido enviado ao servidor...");
                    else
                        Say("O pedido não chegou ao servidor.");
                });

                var nameClear = ValheimUi.CreateButton(nameRow, "Limpar", 70f, 34f, 13);
                nameClear.onClick.AddListener(() =>
                {
                    if (RiderNet.Send(RiderNet.ActionSetName, profile.Id + "\n"))
                        Say("Nome removido (pedido enviado).");
                    else
                        Say("O pedido não chegou ao servidor.");
                });
            }

            // --- Skin (Aesir 30) ---
            if (MountSettings.MountSkinActive?.Value ?? false)
            {
                var skinOptions = profile.SkinOptions();
                if (skinOptions.Count > 1) // there is at least one non-Padrao skin
                {
                    var skinLabel = ValheimUi.CreateLabel(parent,
                        $"Skin: {(string.IsNullOrEmpty(RiderClient.SkinOf(profile.Id)) ? "Padrão" : RiderClient.SkinOf(profile.Id))}",
                        13, ValheimUi.Beige, TextAlignmentOptions.Left);
                    ValheimUi.Anchor((RectTransform)skinLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                        new Vector2(16f, -316f), new Vector2(-16f, -302f));

                    // One button per skin option, in a horizontal strip below the label.
                    float skinRowTop = -320f;
                    float skinRowBottom = -356f;
                    var skinRow = ValheimUi.CreateRect("SkinRow", parent);
                    ValheimUi.Anchor(skinRow, new Vector2(0f, 1f), new Vector2(1f, 1f),
                        new Vector2(16f, skinRowBottom), new Vector2(-16f, skinRowTop));
                    var skinLayout = skinRow.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    skinLayout.spacing = 6f;
                    skinLayout.childControlWidth = true;
                    skinLayout.childControlHeight = true;
                    skinLayout.childForceExpandWidth = false;
                    skinLayout.childForceExpandHeight = true;
                    skinLayout.childAlignment = TextAnchor.MiddleLeft;

                    string currentSkin = RiderClient.SkinOf(profile.Id);
                    foreach (var (skinId, skinColor) in skinOptions)
                    {
                        string id = skinId; // capture for lambda
                        bool isActive = string.IsNullOrEmpty(currentSkin)
                            ? id == MountProfile.DefaultSkinId
                            : string.Equals(id, currentSkin, StringComparison.OrdinalIgnoreCase);

                        var skinBtn = ValheimUi.CreateButton(skinRow, id, 0f, 34f, 12);
                        if (skinBtn.image != null)
                        {
                            // Use the skin color as a hint on the button background.
                            skinBtn.image.color = isActive ? Color.white : new Color(skinColor.r, skinColor.g, skinColor.b, 0.85f);
                        }
                        var btnEl = skinBtn.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                        btnEl.preferredWidth = 80f;
                        btnEl.flexibleWidth = 1f;
                        skinBtn.interactable = !isActive;
                        skinBtn.onClick.AddListener(() =>
                        {
                            string value = id == MountProfile.DefaultSkinId ? "" : id;
                            if (RiderNet.Send(RiderNet.ActionSetSkin, profile.Id + "\n" + value))
                                Say("Skin enviada ao servidor...");
                            else
                                Say("O pedido não chegou ao servidor.");
                        });
                    }
                }
            }
        }

        private static string StateOf(MountProfile profile, MountProfile selected)
        {
            if (!MountRoster.Known) return "Consultando o servidor...";
            if (!MountRoster.Owns(profile)) return "Não possui · à venda no Mestre das Montarias";
            if (!MountRoster.CanRide(profile)) return $"Exige {RidingRanks.NameOf(profile.MinRank)}";
            if (!MountRoster.BossDone(profile)) return $"Exige o passe de {MountRoster.BossName(profile)}";
            return profile == selected ? "Selecionada · pronta" : "Pronta";
        }

        private static bool IsSummoned(MountProfile profile) =>
            MountControl.FindOwned(Player.m_localPlayer, profile.IsInstance) != null;

        // ---- Habilidade ----

        private RectTransform BuildSkill(Transform parent)
        {
            var root = ValheimUi.CreateRect("Skill", parent, false);
            ValheimUi.Stretch(root, 0f, 0f);

            _skillSummary = ValheimUi.CreateLabel(root, "", 18, ValheimUi.Yellow, TextAlignmentOptions.Center);
            ValheimUi.Anchor((RectTransform)_skillSummary.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -30f), Vector2.zero);

            var pane = ValheimUi.CreateInlay(root, "Levels");
            ValheimUi.Anchor(pane, Vector2.zero, Vector2.one, new Vector2(0f, 48f), new Vector2(0f, -40f));
            var header = ValheimUi.CreateLabel(pane, "Habilidade de Montaria", 18, ValheimUi.Orange,
                TextAlignmentOptions.Center, display: true);
            ValheimUi.Anchor((RectTransform)header.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(6f, -36f), new Vector2(-6f, -6f));
            var area = ValheimUi.CreateRect("Area", pane);
            ValheimUi.Anchor(area, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -38f));
            _skillList = ValheimUi.CreateScrollList(area, spacing: 4f);

            var footer = ValheimUi.CreateLabel(root,
                "Aprenda com o Mestre das Montarias. Cada montaria exige um nível; os níveis acima " +
                "deixam todas as suas montarias mais rápidas.",
                14, ValheimUi.Muted, TextAlignmentOptions.Center);
            ValheimUi.Anchor((RectTransform)footer.transform, Vector2.zero, new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, 42f));
            return root;
        }

        private void RebuildSkill()
        {
            if (_skillList == null) return;
            ClearChildren(_skillList);

            int rank = MountRoster.Rank;
            _skillSummary.text = !MountRoster.Known
                ? "<color=#9a9188>Consultando o servidor...</color>"
                : rank <= 0
                    ? "Você ainda não sabe montar."
                    : $"<color=#9a9188>Sua habilidade:</color> {RidingRanks.NameOf(rank)} " +
                      $"<color=#9a9188>(velocidade {RidingRanks.SpeedOf(rank) * 100f:0}%{CastText(RidingRanks.CastOf(rank))})</color>";

            foreach (var level in RidingRanks.All)
            {
                var row = Row(_skillList, 52f);
                ValheimUi.CreateItemIcon(row, SkillIcon, 36f);
                var label = ValheimUi.CreateLabel(row,
                    $"{level.Name}\n<size=12><color=#9a9188>velocidade {level.Speed * 100f:0}%{CastText(level.Cast)}</color></size>",
                    16, ValheimUi.Beige, TextAlignmentOptions.Left);
                Flexible(label.gameObject);

                bool learned = level.Level <= rank;
                bool next = level.Level == rank + 1;
                var tag = ValheimUi.CreateLabel(row, learned ? "Aprendida" : next ? "Próxima" : "Bloqueada", 15,
                    learned ? ValheimUi.Yellow : next ? ValheimUi.Orange : ValheimUi.Muted,
                    TextAlignmentOptions.Right);
                ValheimUi.SetWidth(tag.gameObject, 140f);
            }
        }

        // ---- Admin ----

        private RectTransform BuildAdmin(Transform parent)
        {
            var root = ValheimUi.CreateRect("Admin", parent, false);
            ValheimUi.Stretch(root, 0f, 0f);

            var left = ValheimUi.CreateInlay(root, "List");
            ValheimUi.Anchor(left, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(240f, 0f));
            var listArea = ValheimUi.CreateRect("Area", left);
            ValheimUi.Anchor(listArea, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            _adminList = ValheimUi.CreateScrollList(listArea, spacing: 6f);

            var right = ValheimUi.CreateInlay(root, "Editor");
            ValheimUi.Anchor(right, Vector2.zero, Vector2.one, new Vector2(252f, 0f), Vector2.zero);
            var editorArea = ValheimUi.CreateRect("Area", right);
            ValheimUi.Anchor(editorArea, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            _adminEditor = ValheimUi.CreateScrollList(editorArea, spacing: 8f);
            return root;
        }

        private void RebuildAdmin()
        {
            if (_adminList == null) return;
            ClearChildren(_adminList);
            ClearChildren(_adminEditor);

            if (!Access.IsAdmin())
            {
                var denied = ValheimUi.CreateLabel(_adminEditor, "Apenas administradores.", 18,
                    ValheimUi.Beige, TextAlignmentOptions.Center);
                ValheimUi.SetHeight(denied.gameObject, 40f);
                return;
            }

            var unlock = ValheimUi.CreateButton(_adminList, UnlockLabel(), 0f, 42f, 14);
            unlock.onClick.AddListener(() =>
            {
                if (MountSettings.UnlockAll != null)
                    MountSettings.UnlockAll.Value = !MountSettings.UnlockAll.Value;
                MountHub.ApplySpeedAll();
                RebuildAdmin();
            });

            var all = MountSettings.All;
            if (all == null) return;
            if (_adminProfile == null) _adminProfile = all.Length > 0 ? all[0] : null;
            foreach (var profile in all)
            {
                if (profile == null) continue;
                var button = ValheimUi.CreateButton(_adminList, profile.DefaultName, 0f, 40f, 15);
                button.onClick.AddListener(() =>
                {
                    _adminProfile = profile;
                    RebuildAdmin();
                });
                if (button.image != null)
                    button.image.color = profile == _adminProfile ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);
            }

            var selected = _adminProfile;
            if (selected == null) return;

            Heading("Nome");
            var nameRow = Row(_adminEditor, 40f);
            var nameField = ValheimUi.CreateInputField(nameRow, selected.Name, 200f, 38f);
            Flexible(nameField.gameObject);
            var rename = ValheimUi.CreateButton(nameRow, "Renomear", 120f, 38f, 14);
            rename.onClick.AddListener(() =>
            {
                if (selected.CustomName == null) return;
                selected.CustomName.Value = Sanitize(nameField.text, selected.DefaultName);
                selected.ApplyAll?.Invoke();
                Say("Nome atualizado.");
            });

            AddStat("Velocidade", selected.RunSpeed, 3f, 30f);
            AddStat("Pulo", selected.JumpHeight, 3f, 20f);
            AddStat("Escala", selected.Scale, 0.5f, 3f);
            AddStat("Abertura das pernas (graus)", selected.LegSpread, 0f, 45f);
            AddStat("Rotação da sela (graus)", selected.SaddleYRotation, -360f, 360f);
            AddIntStat("Habilidade exigida (nível)", selected.RequiredRank, 1, Mathf.Max(1, RidingRanks.Count));

            var apply = ValheimUi.CreateButton(_adminEditor, "Aplicar stats", 0f, 42f, 15);
            apply.onClick.AddListener(() =>
            {
                selected.ApplyAll?.Invoke();
                Say("Ajustes aplicados.");
            });

            Heading("Minha ficha (no servidor)");
            var grant = ValheimUi.CreateButton(_adminEditor, "Dar esta montaria a mim", 0f, 42f, 15);
            grant.onClick.AddListener(() => AdminAction(RiderNet.ActionGrantMount, selected.Id));
            var nextRank = ValheimUi.CreateButton(_adminEditor, "Aprender o próximo nível da habilidade", 0f, 42f, 15);
            nextRank.onClick.AddListener(() => AdminAction(RiderNet.ActionNextRank, ""));
            var reset = ValheimUi.CreateButton(_adminEditor, "Zerar minha habilidade e montarias", 0f, 42f, 15);
            reset.onClick.AddListener(() => AdminAction(RiderNet.ActionReset, ""));
        }

        private void AdminAction(string action, string argument)
        {
            Say(RiderNet.Send(action, argument) ? "Pedido enviado ao servidor..." : "O pedido não chegou ao servidor.");
        }

        private void AddStat(string label, ConfigEntry<float> entry, float min, float max)
        {
            if (entry == null) return;
            Heading($"{label}: {entry.Value:0.0}");
            var row = Row(_adminEditor, 40f);
            var field = ValheimUi.CreateInputField(row, entry.Value.ToString("0.0", CultureInfo.InvariantCulture), 100f, 38f);
            Flexible(field.gameObject);
            var save = ValheimUi.CreateButton(row, "OK", 70f, 38f, 14);
            save.onClick.AddListener(() =>
            {
                if (!float.TryParse(field.text.Replace(',', '.'), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out float value))
                {
                    Say("Valor inválido.");
                    return;
                }
                entry.Value = Mathf.Clamp(value, min, max);
                field.text = entry.Value.ToString("0.0", CultureInfo.InvariantCulture);
                Say($"{label} = {entry.Value:0.0}");
            });
        }

        private void AddIntStat(string label, ConfigEntry<int> entry, int min, int max)
        {
            if (entry == null) return;
            Heading($"{label}: {entry.Value}");
            var row = Row(_adminEditor, 40f);
            var field = ValheimUi.CreateInputField(row, entry.Value.ToString(CultureInfo.InvariantCulture), 100f, 38f);
            Flexible(field.gameObject);
            var save = ValheimUi.CreateButton(row, "OK", 70f, 38f, 14);
            save.onClick.AddListener(() =>
            {
                if (!int.TryParse(field.text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                {
                    Say("Valor inválido.");
                    return;
                }
                entry.Value = Mathf.Clamp(value, min, max);
                field.text = entry.Value.ToString(CultureInfo.InvariantCulture);
                Say($"{label} = {entry.Value}");
            });
        }

        private void Heading(string text)
        {
            var label = ValheimUi.CreateLabel(_adminEditor, text, 16, ValheimUi.Orange,
                TextAlignmentOptions.Left, display: true);
            ValheimUi.SetHeight(label.gameObject, 22f);
        }

        private static string UnlockLabel() =>
            MountRoster.UnlockAll ? "Liberar todas: LIGADO" : "Liberar todas: DESLIGADO";

        // ---- helpers ----

        private static RectTransform Row(Transform parent, float height)
        {
            var row = ValheimUi.CreateRect("Row", parent);
            var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;
            layout.childAlignment = TextAnchor.MiddleLeft;
            ValheimUi.SetHeight(row.gameObject, height);
            return row;
        }

        private static void Flexible(GameObject go)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
        }

        private static float Value(ConfigEntry<float> entry) => entry != null ? entry.Value : 0f;

        /// <summary>", conjuração -25%" for a level that shortens the cast, nothing otherwise.</summary>
        private static string CastText(float cast) =>
            cast < 0.999f ? $", conjuração -{(1f - cast) * 100f:0}%" : "";

        private static void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }
        }

        private void Say(string text)
        {
            if (_status != null) _status.text = text ?? "";
        }

        private Sprite IconOf(MountProfile profile)
        {
            if (profile == null) return null;
            string key = profile.Id;
            if (_icons.TryGetValue(key, out var cached) && cached != null)
                return cached;
            var sprite = !string.IsNullOrEmpty(profile.IconFile) ? LoadSprite(profile.IconFile) : null;
            if (sprite == null)
                sprite = Prefabs.WhistleItem.IconFor(profile);
            _icons[key] = sprite;
            return sprite;
        }

        internal static Sprite LoadSprite(string fileName)
        {
            string path = FindPng(fileName);
            if (path == null)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: ícone não encontrado ({fileName})");
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            if (!TryLoadImage(tex, File.ReadAllBytes(path)))
                return null;
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static string FindPng(string fileName)
        {
            string root = Path.Combine(Paths.PluginPath, "ValheimMontarias", "Assets");
            foreach (var folder in new[] { "Menu", "menu", "" })
            {
                string path = string.IsNullOrEmpty(folder)
                    ? Path.Combine(root, fileName)
                    : Path.Combine(root, folder, fileName);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static bool TryLoadImage(Texture2D tex, byte[] bytes)
        {
            try
            {
                var type = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule", false);
                var method = type?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
                if (method == null) return false;
                return (bool)method.Invoke(null, new object[] { tex, bytes });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"ValheimMontarias: LoadImage failed: {e.Message}");
                return false;
            }
        }

        private static string HintText()
        {
            string menu = MountSettings.MenuKeyName();
            return $"{menu} abre/fecha  ·  Use o item para montar/desmontar  ·  Esc fecha";
        }

        private static string Sanitize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            value = value.Replace("\n", " ").Replace("\r", " ").Trim();
            if (value.Length > 32) value = value.Substring(0, 32).Trim();
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static bool Typing()
        {
            if ((Chat.instance != null && Chat.instance.HasFocus()) || Console.IsVisible() ||
                (TextInput.instance != null && TextInput.IsVisible()))
                return true;

            var es = EventSystem.current;
            GameObject selected = es != null ? es.currentSelectedGameObject : null;
            if (!selected) return false;

            TMP_InputField tmpInput = selected.GetComponentInParent<TMP_InputField>();
            if (tmpInput && tmpInput.isFocused) return true;

            InputField input = selected.GetComponentInParent<InputField>();
            return input && input.isFocused;
        }

        private static bool BusyElsewhere()
        {
            if (Typing() || Menu.IsVisible()) return true;
            var player = Player.m_localPlayer;
            return player != null && player.IsDead();
        }
    }
}
