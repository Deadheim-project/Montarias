using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ValheimMontarias.Prefabs;
using ValheimMontarias.UI;

namespace ValheimMontarias
{
    internal sealed class MountMenu : MonoBehaviour
    {
        private enum Tab { Mine, Admin }

        private static MountMenu _instance;
        private GameObject _canvas;
        private RectTransform _mineRoot;
        private RectTransform _mineList;
        private RectTransform _adminRoot;
        private RectTransform _adminList;
        private RectTransform _adminEditor;
        private TextMeshProUGUI _status;
        private TextMeshProUGUI _header;
        private readonly List<Button> _tabButtons = new List<Button>();
        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<string, Sprite> _icons = new Dictionary<string, Sprite>();
        private bool _open;
        private Tab _tab = Tab.Mine;
        private MountProfile _adminProfile;

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
            else _instance.OpenInternal(Tab.Mine);
        }

        public static void Open() => _instance?.OpenInternal(Tab.Mine);

        public static void OpenAdmin(MountProfile profile = null) =>
            _instance?.OpenInternal(Tab.Admin, profile);

        public static void Close() => _instance?.CloseInternal();

        public static MountProfile Selected()
        {
            var owned = MountRoster.Owned(Player.m_localPlayer);
            if (owned.Count == 0) return null;
            string id = MountSettings.SelectedMount != null ? MountSettings.SelectedMount.Value : null;
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i] != null && owned[i].Id == id)
                    return owned[i];
            }
            return owned[0];
        }

        public static void Select(MountProfile profile)
        {
            if (profile == null || MountSettings.SelectedMount == null) return;
            MountSettings.SelectedMount.Value = profile.Id;
            _instance?.RebuildMine();
        }

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

                if (MountSettings.SummonKey != null
                    && MountSettings.SummonKey.Value.IsDown()
                    && !BusyElsewhere())
                {
                    var chosen = Selected();
                    if (_open) CloseInternal();
                    JavaliControl.TrySummonProfile(Player.m_localPlayer, chosen);
                }
            }

            if (!_open) return;
            if (Player.m_localPlayer.IsDead() || Console.IsVisible())
            {
                CloseInternal();
                return;
            }

            if (!Typing() && Input.GetKeyDown(KeyCode.Escape))
                CloseInternal();
        }

        private void LateUpdate()
        {
            if (!_open) return;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void OpenInternal(Tab tab, MountProfile adminProfile = null)
        {
            if (Player.m_localPlayer == null || BusyElsewhere()) return;
            if (tab == Tab.Admin && !Access.IsAdmin())
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Apenas administradores.");
                if (!_open) return;
                tab = Tab.Mine;
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
            }

            _adminProfile = adminProfile ?? _adminProfile ?? MountSettings.Javali;
            SetTab(tab);
        }

        private void BuildWindow()
        {
            var panel = ValheimUi.CreatePanel(_canvas.transform, 940f, 640f);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;

            var titleBar = ValheimUi.CreateRect("TitleBar", panel);
            ValheimUi.Anchor(titleBar, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(0f, -56f), Vector2.zero);
            titleBar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            titleBar.gameObject.AddComponent<DragWindow>().Target = panel;

            _header = ValheimUi.CreateLabel(titleBar, "Montarias", 28, ValheimUi.Orange,
                TextAlignmentOptions.Center, display: true);
            ValheimUi.Stretch((RectTransform)_header.transform, 60f, 8f);

            var close = ValheimUi.CreateButton(panel, "X", 36f, 36f, 18);
            ValheimUi.Anchor((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-52f, -52f), new Vector2(-16f, -16f));
            close.onClick.AddListener(CloseInternal);

            var tabStrip = ValheimUi.CreateRect("Tabs", panel);
            ValheimUi.Anchor(tabStrip, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(24f, -104f), new Vector2(-24f, -60f));
            var strip = tabStrip.gameObject.AddComponent<HorizontalLayoutGroup>();
            strip.spacing = 8f;
            strip.childControlWidth = true;
            strip.childControlHeight = true;
            strip.childForceExpandWidth = false;
            strip.childAlignment = TextAnchor.MiddleLeft;

            _tabButtons.Clear();
            AddTab(tabStrip, "Minhas Montarias", Tab.Mine, 230f);
            AddTab(tabStrip, "Admin", Tab.Admin, 120f);

            var content = ValheimUi.CreateRect("Content", panel);
            ValheimUi.Anchor(content, Vector2.zero, Vector2.one,
                new Vector2(24f, 52f), new Vector2(-24f, -110f));

            _mineRoot = BuildMine(content);
            _adminRoot = BuildAdmin(content);

            _status = ValheimUi.CreateLabel(panel, HintText(), 14, ValheimUi.Muted, TextAlignmentOptions.Center);
            ValheimUi.Anchor((RectTransform)_status.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 14f), new Vector2(-26f, 44f));
        }

        private void AddTab(Transform parent, string label, Tab tab, float width)
        {
            var button = ValheimUi.CreateButton(parent, label, width, 40f, 16);
            button.onClick.AddListener(() => SetTab(tab));
            _tabButtons.Add(button);
        }

        private RectTransform BuildMine(Transform parent)
        {
            var root = ValheimUi.CreateRect("Mine", parent);
            ValheimUi.Stretch(root, 0f, 0f);

            var frame = ValheimUi.CreateInlay(root, "MineFrame");
            ValheimUi.Stretch(frame, 0f, 0f);

            var area = ValheimUi.CreateRect("Area", frame);
            ValheimUi.Anchor(area, Vector2.zero, Vector2.one, new Vector2(6f, 6f), new Vector2(-6f, -6f));
            _mineList = ValheimUi.CreateScrollList(area, spacing: 6f);
            return root;
        }

        private RectTransform BuildAdmin(Transform parent)
        {
            var root = ValheimUi.CreateRect("Admin", parent, false);
            ValheimUi.Stretch(root, 0f, 0f);

            var frame = ValheimUi.CreateInlay(root, "AdminFrame");
            ValheimUi.Stretch(frame, 0f, 0f);

            var left = ValheimUi.CreateRect("List", frame);
            ValheimUi.Anchor(left, new Vector2(0f, 0f), new Vector2(0f, 1f),
                new Vector2(10f, 10f), new Vector2(250f, -10f));
            _adminList = ValheimUi.CreateScrollList(left, spacing: 6f);

            var right = ValheimUi.CreateRect("Editor", frame);
            ValheimUi.Anchor(right, Vector2.zero, Vector2.one, new Vector2(258f, 10f), new Vector2(-10f, -10f));
            _adminEditor = ValheimUi.CreateScrollList(right, spacing: 8f);
            return root;
        }

        private void SetTab(Tab tab)
        {
            if (tab == Tab.Admin && !Access.IsAdmin())
            {
                Say("Apenas administradores.");
                tab = Tab.Mine;
            }

            _tab = tab;
            if (_mineRoot != null) _mineRoot.gameObject.SetActive(tab == Tab.Mine);
            if (_adminRoot != null) _adminRoot.gameObject.SetActive(tab == Tab.Admin);

            for (int i = 0; i < _tabButtons.Count; i++)
            {
                var button = _tabButtons[i];
                bool on = (Tab)i == tab;
                var text = button.GetComponentInChildren<TextMeshProUGUI>();
                if (text != null) text.color = on ? ValheimUi.Yellow : ValheimUi.Orange;
                if (button.image != null)
                    button.image.color = on ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);
            }

            if (_header != null)
            {
                _header.text = tab == Tab.Mine ? "Minhas Montarias" : "Admin";
            }

            if (tab == Tab.Mine) RebuildMine();
            else RebuildAdmin();
            Say(HintText());
        }

        private void RebuildMine()
        {
            ClearChildren(_mineList);
            var player = Player.m_localPlayer;
            var owned = MountRoster.Owned(player);

            if (owned.Count == 0)
            {
                var empty = ValheimUi.CreateLabel(_mineList,
                    "Você ainda não possui nenhuma montaria.",
                    18, ValheimUi.Beige, TextAlignmentOptions.Center);
                ValheimUi.SetHeight(empty.gameObject, 80f);
            }
            else
            {
                var selected = Selected();
                for (int i = 0; i < owned.Count; i++)
                {
                    var profile = owned[i];
                    if (profile == null) continue;
                    var button = ValheimUi.CreateButton(_mineList, "", 0f, 72f, 18);
                    button.onClick.AddListener(() => Select(profile));
                    Track(button.gameObject);
                    if (button.image != null)
                        button.image.color = profile == selected ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);

                    var icon = ValheimUi.CreateRect("Icon", button.transform);
                    ValheimUi.Anchor(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                        new Vector2(8f, -28f), new Vector2(64f, 28f));
                    var image = icon.gameObject.AddComponent<Image>();
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    image.sprite = IconOf(profile.IconFile);
                    image.enabled = image.sprite != null;

                    var label = button.GetComponentInChildren<TextMeshProUGUI>();
                    if (label != null)
                    {
                        ValheimUi.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one,
                            new Vector2(76f, 4f), new Vector2(-12f, -4f));
                        label.alignment = TextAlignmentOptions.Left;
                        label.text = profile == selected ? $"{profile.Name}   (selecionada)" : profile.Name;
                        label.color = ValheimUi.Beige;
                    }
                }
            }
        }

        private void RebuildAdmin()
        {
            ClearChildren(_adminList);
            ClearChildren(_adminEditor);

            if (!Access.IsAdmin())
            {
                var denied = ValheimUi.CreateLabel(_adminEditor, "Apenas administradores.", 18,
                    ValheimUi.Beige, TextAlignmentOptions.Center);
                ValheimUi.SetHeight(denied.gameObject, 40f);
                return;
            }

            var unlock = ValheimUi.CreateButton(_adminList,
                UnlockLabel(), 0f, 42f, 14);
            unlock.onClick.AddListener(() =>
            {
                if (MountSettings.UnlockAll != null)
                    MountSettings.UnlockAll.Value = !MountSettings.UnlockAll.Value;
                RebuildAdmin();
                RebuildMine();
            });
            Track(unlock.gameObject);

            var all = MountSettings.All;
            if (all == null) return;
            if (_adminProfile == null) _adminProfile = all.Length > 0 ? all[0] : null;

            for (int i = 0; i < all.Length; i++)
            {
                var profile = all[i];
                if (profile == null) continue;
                var button = ValheimUi.CreateButton(_adminList, profile.DefaultName, 0f, 40f, 15);
                button.onClick.AddListener(() =>
                {
                    _adminProfile = profile;
                    RebuildAdmin();
                });
                if (button.image != null)
                    button.image.color = profile == _adminProfile ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);
                Track(button.gameObject);
            }

            var selected = _adminProfile;
            if (selected == null) return;

            Heading("Nome");
            var nameRow = Row(40f);
            var nameField = ValheimUi.CreateInputField(nameRow, selected.Name, 200f, 38f);
            Flex(nameField.gameObject);
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
            AddStat("Vida", selected.MaxHealth, 10f, 1000f);
            AddStat("Stamina", selected.MaxStamina, 10f, 1000f);
            AddStat("Dreno", selected.StaminaDrain, 0f, 40f);

            var grant = ValheimUi.CreateButton(_adminEditor, "Dar esta montaria a mim", 0f, 42f, 15);
            grant.onClick.AddListener(() =>
            {
                MountRoster.Grant(Player.m_localPlayer, selected);
                Select(selected);
                Say($"{selected.Name} liberada para você.");
                RebuildMine();
            });
            Track(grant.gameObject);

            var apply = ValheimUi.CreateButton(_adminEditor, "Aplicar stats", 0f, 42f, 15);
            apply.onClick.AddListener(() =>
            {
                selected.ApplyAll?.Invoke();
                Say("Ajustes aplicados.");
            });
            Track(apply.gameObject);
        }

        private void AddStat(string label, BepInEx.Configuration.ConfigEntry<float> entry, float min, float max)
        {
            if (entry == null) return;
            Heading($"{label}: {entry.Value:0.0}");
            var row = Row(40f);
            var field = ValheimUi.CreateInputField(row, entry.Value.ToString("0.0", CultureInfo.InvariantCulture),
                100f, 38f);
            Flex(field.gameObject);
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

        private void Heading(string text)
        {
            var label = ValheimUi.CreateLabel(_adminEditor, text, 16, ValheimUi.Orange,
                TextAlignmentOptions.Left, display: true);
            ValheimUi.SetHeight(label.gameObject, 22f);
        }

        private RectTransform Row(float height)
        {
            var row = ValheimUi.CreateRect("Row", _adminEditor);
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

        private static void Flex(GameObject go)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            element.flexibleWidth = 1f;
        }

        private static string UnlockLabel()
        {
            bool on = MountSettings.UnlockAll != null && MountSettings.UnlockAll.Value;
            return on ? "Liberar todas: LIGADO" : "Liberar todas: DESLIGADO";
        }

        private void CloseInternal()
        {
            if (!_open) return;
            _open = false;
            _tab = Tab.Mine;
            _tabButtons.Clear();
            ClearTracked();
            _mineRoot = null;
            _mineList = null;
            _adminRoot = null;
            _adminList = null;
            _adminEditor = null;
            _status = null;
            _header = null;
            if (_canvas != null) Destroy(_canvas);
            _canvas = null;

            if (InventoryGui.instance != null && InventoryGui.IsVisible()) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void Track(GameObject go)
        {
            if (go != null) _spawned.Add(go);
        }

        private void ClearChildren(RectTransform content)
        {
            if (content == null) return;
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                var child = content.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }
        }

        private void ClearTracked()
        {
            _spawned.Clear();
        }

        private void Say(string text)
        {
            if (_status != null) _status.text = text ?? "";
        }

        private Sprite IconOf(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            if (_icons.TryGetValue(fileName, out var cached) && cached != null)
                return cached;
            var sprite = LoadSprite(fileName);
            _icons[fileName] = sprite;
            return sprite;
        }

        private static Sprite LoadSprite(string fileName)
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
            var folders = new[] { "Menu", "menu", "" };
            for (int i = 0; i < folders.Length; i++)
            {
                string path = string.IsNullOrEmpty(folders[i])
                    ? Path.Combine(root, fileName)
                    : Path.Combine(root, folders[i], fileName);
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
            string menu = MountSettings.MenuKey != null ? MountSettings.MenuKey.Value.MainKey.ToString() : "U";
            string summon = MountSettings.SummonKey != null ? MountSettings.SummonKey.Value.MainKey.ToString() : "H";
            return $"{menu} fecha  ·  {summon} invoca  ·  Esc fecha";
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
            var es = EventSystem.current;
            if (es == null || es.currentSelectedGameObject == null) return false;
            return es.currentSelectedGameObject.GetComponent<TMP_InputField>() != null
                || es.currentSelectedGameObject.GetComponent<InputField>() != null;
        }

        private static bool BusyElsewhere()
        {
            if (Console.IsVisible()) return true;
            if (TextInput.IsVisible()) return true;
            var player = Player.m_localPlayer;
            return player != null && player.IsDead();
        }
    }
}
