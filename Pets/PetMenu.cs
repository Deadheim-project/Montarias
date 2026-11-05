using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ValheimMontarias.UI;

namespace ValheimMontarias.Pets
{
    /// <summary>
    /// Menu de pets, na mesma janela do menu de montarias: lista à esquerda (todos os pets do
    /// catálogo do Deadheim, com o estado de cada um) e, à direita, o detalhe com os bônus e o
    /// botão de invocar/guardar. Os pets desbloqueados são o registro do personagem (o item do pet
    /// é consumido ao usar); a lista e a invocação são do Deadheim.Pets (PetBridge).
    /// </summary>
    internal sealed class PetMenu : MonoBehaviour
    {
        private const float Width = 940f;
        private const float Height = 640f;

        private static PetMenu _instance;

        private GameObject _canvas;
        private TextMeshProUGUI _status;
        private RectTransform _list;
        private RectTransform _detail;
        private bool _open;
        private string _focus;
        private string _lastActive;
        private string _lastBuffPet = "";
        private float _nextRefresh;
        private string _signature = "";

        public static bool IsOpen => _instance != null && _instance._open;

        public static void EnsureCreated()
        {
            if (_instance != null) return;
            var go = new GameObject("ValheimMontarias_PetMenu");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<PetMenu>();
        }

        // Também chamados por reflexão pela Central Deadheim (como MountMenu.Open).
        public static void Toggle()
        {
            if (_instance == null) return;
            if (_instance._open) _instance.CloseInternal();
            else _instance.OpenInternal();
        }

        public static void Open() => _instance?.OpenInternal();
        public static void Close() => _instance?.CloseInternal();

        private void Update()
        {
            if (Player.m_localPlayer == null)
            {
                if (_open) CloseInternal();
                return;
            }

            if (!MountMenu.Typing() && !Console.IsVisible() && PetSettings.MenuKey != null && PetSettings.MenuKey.Value.IsDown())
                Toggle();

            // O peso total do inventário fica em cache até o inventário mudar: ao trocar de pet
            // (ou entrar/sair de arena) força o recálculo para o bônus de madeira aparecer na hora.
            string buffPet = PetBridge.ActiveIdForBuffs();
            if (buffPet != _lastBuffPet)
            {
                _lastBuffPet = buffPet;
                Player.m_localPlayer.GetInventory()?.Changed();
            }

            if (!_open) return;
            if (Player.m_localPlayer.IsDead() || Console.IsVisible())
            {
                CloseInternal();
                return;
            }

            if (!MountMenu.Typing() && Input.GetKeyDown(KeyCode.Escape))
            {
                CloseInternal();
                return;
            }

            // O estado do pet muda por RPC (resposta do servidor): consulta a cada 0,25 s, mas só
            // redesenha (destrói/recria a lista) se o estado visível mudou, senão cliques se perdem.
            string active = PetBridge.ActiveId();
            if (active != _lastActive || Time.unscaledTime > _nextRefresh)
            {
                _lastActive = active;
                _nextRefresh = Time.unscaledTime + 0.25f;
                Redraw();
            }
        }

        private void LateUpdate()
        {
            if (!_open) return;
            if (ZCursor.LockState != CursorLockMode.None) ZCursor.LockState = CursorLockMode.None;
            if (!ZCursor.IsVisible) ZCursor.Show();
            if (!Cursor.visible) Cursor.visible = true;
        }

        private void OpenInternal()
        {
            if (_open || Player.m_localPlayer == null || MountMenu.IsOpen || MountMenu.BusyElsewhere()) return;
            if (!PetBridge.Available)
            {
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Os pets vêm do mod Deadheim, que não está carregado.");
                return;
            }
            if (!ValheimUi.EnsureAssets()) return;

            _canvas = ValheimUi.CreateCanvas("ValheimMontarias_PetWindow", 4900);
            if (_canvas == null) return;
            BuildWindow();
            _open = true;
            _signature = "";
            _lastActive = PetBridge.ActiveId();
            _nextRefresh = Time.unscaledTime + 1f;
            if (string.IsNullOrEmpty(_focus)) _focus = _lastActive.Length > 0 ? _lastActive : FirstId();
            Redraw();
        }

        private void CloseInternal()
        {
            if (!_open) return;
            _open = false;
            _list = _detail = null;
            _status = null;
            if (_canvas != null) Destroy(_canvas);
            _canvas = null;
            if (MountMenu.IsOpen || AdminMenu.IsOpen || Menu.IsVisible() ||
                (InventoryGui.instance != null && InventoryGui.IsVisible())) return;
            ZCursor.LockState = CursorLockMode.Locked;
            ZCursor.Hide();
        }

        private static string FirstId()
        {
            var ids = PetBridge.CatalogIds();
            return ids.Length > 0 ? ids[0] : "";
        }

        private void BuildWindow()
        {
            var panel = ValheimUi.CreatePanel(_canvas.transform, Width, Height);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;

            var titleBar = ValheimUi.CreateRect("TitleBar", panel);
            ValheimUi.Anchor(titleBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -58f), Vector2.zero);
            titleBar.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            titleBar.gameObject.AddComponent<DragWindow>().Target = panel;

            var title = ValheimUi.CreateLabel(titleBar, "Pets", 30, ValheimUi.Orange, TextAlignmentOptions.Center, display: true);
            ValheimUi.Stretch((RectTransform)title.transform, 60f, 10f);

            var close = ValheimUi.CreateButton(panel, "X", 36f, 36f, 18);
            ValheimUi.Anchor((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-52f, -52f), new Vector2(-16f, -16f));
            close.onClick.AddListener(CloseInternal);

            var content = ValheimUi.CreateRect("Content", panel);
            ValheimUi.Anchor(content, Vector2.zero, Vector2.one, new Vector2(24f, 52f), new Vector2(-24f, -70f));

            _status = ValheimUi.CreateLabel(panel, HintText(), 15, ValheimUi.Yellow, TextAlignmentOptions.Left);
            ValheimUi.Anchor((RectTransform)_status.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(26f, 14f), new Vector2(-26f, 44f));

            var left = ValheimUi.CreateInlay(content, "List");
            ValheimUi.Anchor(left, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(320f, 0f));
            var area = ValheimUi.CreateRect("Area", left);
            ValheimUi.Anchor(area, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            _list = ValheimUi.CreateScrollList(area, spacing: 4f);

            _detail = ValheimUi.CreateInlay(content, "Detail");
            ValheimUi.Anchor(_detail, Vector2.zero, Vector2.one, new Vector2(332f, 0f), Vector2.zero);
        }

        private static string HintText()
        {
            string quick = PetSettings.QuickKey != null && PetSettings.QuickKey.Value.MainKey != KeyCode.None
                ? PetSettings.QuickKey.Value.ToString() : null;
            return (quick != null ? quick + " invoca/guarda o pet  ·  " : "") +
                   "Use o item do pet no inventário para desbloquear  ·  Esc fecha";
        }

        private enum State { Active, Unlocked, Item, Locked }

        private static State StateOf(string id, string active)
        {
            if (PetBridge.IsUnlocked(id)) return id == active ? State.Active : State.Unlocked;
            return PetBridge.HasItem(id) ? State.Item : State.Locked;
        }

        private static string StateText(State s)
        {
            switch (s)
            {
                case State.Active: return "Ativo";
                case State.Unlocked: return "Desbloqueado";
                case State.Item: return "Item no inventário: use para desbloquear";
                default: return "Bloqueado";
            }
        }

        private string ComputeSignature(string active, string[] ids)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(_focus).Append('|').Append(active).Append('|')
              .Append(PetSettings.BuffsEnabled == null || PetSettings.BuffsEnabled.Value ? '1' : '0');
            foreach (var id in ids) sb.Append('|').Append(id).Append(':').Append((int)StateOf(id, active));
            return sb.ToString();
        }

        private void Redraw(bool force = false)
        {
            if (!_open || _list == null || _detail == null) return;
            string active = PetBridge.ActiveId();
            var ids = PetBridge.CatalogIds();
            if (string.IsNullOrEmpty(_focus) && ids.Length > 0) _focus = ids[0];

            string sig = ComputeSignature(active, ids);
            if (!force && sig == _signature) return;
            _signature = sig;

            Clear(_list);
            foreach (var id in ids)
            {
                string petId = id;
                var state = StateOf(petId, active);
                var row = ValheimUi.CreateButton(_list, "", 0f, 64f, 16);
                row.onClick.AddListener(() => { _focus = petId; Redraw(true); });
                if (row.image != null)
                    row.image.color = petId == _focus ? Color.white : new Color(0.72f, 0.72f, 0.72f, 1f);

                var icon = ValheimUi.CreateItemIcon(row.transform, "Pet_" + petId, 48f);
                var iconRect = (RectTransform)icon.transform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(32f, 0f);
                icon.raycastTarget = false;
                if (state == State.Locked) icon.color = new Color(0.45f, 0.45f, 0.45f, 1f);

                var label = row.GetComponentInChildren<TextMeshProUGUI>();
                if (label != null)
                {
                    ValheimUi.Anchor((RectTransform)label.transform, Vector2.zero, Vector2.one,
                        new Vector2(64f, 4f), new Vector2(-8f, -4f));
                    label.alignment = TextAlignmentOptions.Left;
                    label.color = ValheimUi.Beige;
                    label.text = $"{PetBridge.NameOf(petId)}\n<size=12><color=#9a9188>{StateText(state)}</color></size>";
                }
            }

            RebuildDetail(active);
        }

        private void RebuildDetail(string active)
        {
            Clear(_detail);
            if (string.IsNullOrEmpty(_focus)) return;
            string id = _focus;
            var state = StateOf(id, active);

            var art = ValheimUi.CreateItemIcon(_detail, "Pet_" + id, 140f);
            var artRect = (RectTransform)art.transform;
            artRect.anchorMin = artRect.anchorMax = new Vector2(0f, 1f);
            artRect.pivot = new Vector2(0f, 1f);
            artRect.anchoredPosition = new Vector2(16f, -16f);
            artRect.sizeDelta = new Vector2(140f, 140f);
            art.raycastTarget = false;
            if (state == State.Locked) art.color = new Color(0.45f, 0.45f, 0.45f, 1f);

            var name = ValheimUi.CreateLabel(_detail, PetBridge.NameOf(id), 26, ValheimUi.Orange,
                TextAlignmentOptions.TopLeft, display: true);
            ValheimUi.Anchor((RectTransform)name.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(172f, -56f), new Vector2(-16f, -16f));

            var stateLabel = ValheimUi.CreateLabel(_detail, StateText(state), 16, ValheimUi.Yellow, TextAlignmentOptions.TopLeft);
            ValheimUi.Anchor((RectTransform)stateLabel.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(172f, -84f), new Vector2(-16f, -58f));

            var lines = PetBuffs.Describe(id);
            string buffText;
            if (PetSettings.BuffsEnabled != null && !PetSettings.BuffsEnabled.Value)
                buffText = "Os bônus de pet estão desligados neste servidor.";
            else if (lines.Count == 0)
                buffText = "Este pet é só companhia: não dá bônus.";
            else
                buffText = "<color=#ffa13c>Bônus com o pet ativo</color>\n• " + string.Join("\n• ", lines) +
                           "\n<size=13><color=#9a9188>Não valem em arena nem durante raids, onde o pet fica oculto.</color></size>";
            var buffs = ValheimUi.CreateLabel(_detail, buffText, 17, ValheimUi.Beige, TextAlignmentOptions.TopLeft);
            ValheimUi.Anchor((RectTransform)buffs.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                new Vector2(172f, -270f), new Vector2(-16f, -92f));

            string note = state == State.Item
                ? "Use o item do pet no inventário: ele é consumido e o pet fica desbloqueado para sempre."
                : state == State.Locked
                    ? "Consiga o item deste pet (drops de monstros e Aesir) e use-o para desbloquear."
                    : "";
            if (note.Length > 0)
            {
                var hint = ValheimUi.CreateLabel(_detail, note, 15, ValheimUi.Muted, TextAlignmentOptions.TopLeft);
                ValheimUi.Anchor((RectTransform)hint.transform, new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(16f, 64f), new Vector2(-16f, 120f));
            }

            if (state == State.Active || state == State.Unlocked)
            {
                bool isActive = state == State.Active;
                var button = ValheimUi.CreateButton(_detail, isActive ? "Guardar" : "Invocar", 150f, 40f, 16);
                ValheimUi.Anchor((RectTransform)button.transform, new Vector2(1f, 0f), new Vector2(1f, 0f),
                    new Vector2(-166f, 16f), new Vector2(-16f, 56f));
                button.onClick.AddListener(() =>
                {
                    if (isActive) PetBridge.Dismiss();
                    else PetBridge.Activate(id);
                    Say(isActive ? "Pet guardado." : "Pedido enviado: o pet já aparece.");
                });
            }
        }

        private static void Clear(RectTransform content)
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
    }
}
