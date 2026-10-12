// Driver de teste de ponta a ponta do Montarias com o Mestre das Montarias do NpcValheim.
//
// NUNCA vai para jogador. Um cliente real conecta num servidor dedicado local (run-e2e.ps1)
// e segue o roteiro abaixo. Cada checagem escreve "[MONTTEST] PASS|FAIL <nome> <detalhe>"
// no LogOutput.log do cliente; no fim, "[MONTTEST] DONE pass=N fail=M" e o arquivo
// <sync>\result.txt.
//
// Tudo do Montarias e do NpcValheim e interno: o driver chega neles por reflexao, pelo
// nome, como o proprio NpcValheim faz com a MontariasApi.
//
// Argumentos (depois do +connect/-password que o DirectJoinFlow do Deadheim ja usa):
//   -monttest-sync <dir>   pasta do resultado e das fotos
//   -monttest-save <dir>   pasta dos personagens de teste (nao toca nos do jogador)
using BepInEx;
using BepInEx.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

namespace MontariasTestDriver
{
    [BepInPlugin("Detalhes.MontariasTestDriver", "MontariasTestDriver", "1.0.0")]
    [BepInDependency("com.valheimmontarias.mod")]
    public class Driver : BaseUnityPlugin
    {
        internal const string CharacterName = "Montador";

        private static ManualLogSource _log;

        private string _sync;
        private bool _creatingCharacter;
        private float _characterPanelSince = -1f;
        private bool _started;
        private int _pass;
        private int _fail;

        private Player Me => Player.m_localPlayer;

        private void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            _sync = Arg(args, "-monttest-sync");
            if (string.IsNullOrEmpty(_sync))
            {
                enabled = false;
                return;
            }
            string save = Arg(args, "-monttest-save");
            if (!string.IsNullOrEmpty(save))
            {
                Directory.CreateDirectory(save);
                Utils.SetSaveDataPath(save);
            }
            QualitySettings.globalTextureMipmapLimit = 2;
            Application.targetFrameRate = 30;
            _log = Logger;
            Log($"driver ativo: sync={_sync} save={save}");
        }

        private static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return null;
        }

        private static void Log(string text) => _log?.LogInfo("[MONTTEST] " + text);

        private void Check(string name, bool ok, string detail = "")
        {
            if (ok) _pass++;
            else _fail++;
            Log($"{(ok ? "PASS" : "FAIL")} {name} {detail}");
        }

        private void Update()
        {
            FejdStartup startup = FejdStartup.instance;
            if (startup != null && startup.m_newCharacterPanel != null && startup.m_newCharacterPanel.activeInHierarchy)
            {
                if (_characterPanelSince < 0f) _characterPanelSince = Time.time;
                if (!_creatingCharacter && Time.time - _characterPanelSince > 1.5f)
                {
                    _creatingCharacter = true;
                    startup.m_csNewCharacterName.text = CharacterName;
                    Log("criando personagem " + CharacterName);
                    startup.OnNewCharacterDone(true);
                }
            }

            if (Game.instance != null && Game.instance.InIntro(true) && ZNet.GetConnectionStatus() == ZNet.ConnectionStatus.Connected)
                Game.instance.SkipIntro();

            if (!_started && Me != null && !Me.InCutscene() && !Me.IsTeleporting())
            {
                _started = true;
                StartCoroutine(Run());
            }
        }

        // ------------------------------------------------------------------ reflexao

        private static Type T(string assembly, string type)
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assembly);
            return asm?.GetType(type);
        }

        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        private static object Static(Type type, string member, params object[] args)
        {
            if (type == null) throw new Exception("tipo nao encontrado");
            var prop = type.GetProperty(member, Any);
            if (prop != null && args.Length == 0) return prop.GetValue(null);
            var field = type.GetField(member, Any);
            if (field != null && args.Length == 0) return field.GetValue(null);
            var method = type.GetMethods(Any).FirstOrDefault(m => m.Name == member && m.GetParameters().Length == args.Length);
            if (method == null) throw new Exception($"{type.Name}.{member} nao existe");
            return method.Invoke(null, args);
        }

        private static object Get(object obj, string member)
        {
            var type = obj.GetType();
            var prop = type.GetProperty(member, Any);
            if (prop != null) return prop.GetValue(obj);
            return type.GetField(member, Any)?.GetValue(obj);
        }

        private static Type MontApi => T("ValheimMontarias", "ValheimMontarias.MontariasApi");
        private static Type MontSettings => T("ValheimMontarias", "ValheimMontarias.MountSettings");
        private static Type Javali => T("ValheimMontarias", "ValheimMontarias.JavaliControl");
        private static Type Whistle => T("ValheimMontarias", "ValheimMontarias.Prefabs.WhistleItem");
        private static Type Combat => T("ValheimMontarias", "ValheimMontarias.CombatLock");
        private static Type NpcMontApi => T("NpcValheim", "NpcValheim.Integration.MontariasApi");
        private static Type Catalog => T("NpcValheim", "NpcValheim.Npc.MountCatalog");
        private static Type Trainer => T("NpcValheim", "NpcValheim.Npc.MountTrainer");
        private static Type TrainerNpc => T("NpcValheim", "NpcValheim.Npc.MountTrainerNpc");

        private bool Riding => (bool)Static(Javali, "IsRiding", Me);
        private int Rank => (int)Static(MontApi, "LocalRank");
        private bool OwnsJavali => (bool)Static(MontApi, "LocalOwns", "capivara");

        private ItemDrop.ItemData MountItem() =>
            Me.GetInventory().GetAllItems().FirstOrDefault(i => (bool)Static(Whistle, "IsWhistle", i));

        private int CoinsInBag() =>
            Me.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == "$item_coins").Sum(i => i.m_stack);

        private static IEnumerator Until(Func<bool> done, float timeout)
        {
            float end = Time.time + timeout;
            while (Time.time < end)
            {
                bool ok;
                try { ok = done(); } catch { ok = false; }
                if (ok) yield break;
                yield return new WaitForSeconds(0.25f);
            }
        }

        private IEnumerator Photo(string name)
        {
            yield return new WaitForEndOfFrame();
            string dir = Path.Combine(_sync, "fotos");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
            yield return new WaitForSeconds(0.5f);
        }

        private static List<string> VisibleTexts() =>
            UnityEngine.Object.FindObjectsOfType<TMP_Text>()
                .Where(t => t.isActiveAndEnabled && !string.IsNullOrEmpty(t.text))
                .Select(t => t.text).ToList();

        // --------------------------------------------------------------------- roteiro

        private IEnumerator Run()
        {
            Log("spawn ok");
            yield return new WaitForSeconds(5f);
            File.WriteAllText(Path.Combine(_sync, "spawned"), DateTime.Now.ToString("HH:mm:ss"));

            foreach (var step in new (string, Func<IEnumerator>)[]
                     {
                         ("api", Api),
                         ("mestre", Mestre),
                         ("compra", Compra),
                         ("montar", Montar),
                         ("combate", Combate),
                     })
            {
                Log("=== " + step.Item1);
                yield return RunSafely(step.Item2(), step.Item1);
            }

            Log($"DONE pass={_pass} fail={_fail}");
            File.WriteAllText(Path.Combine(_sync, "result.txt"), $"pass={_pass} fail={_fail}");
        }

        private IEnumerator RunSafely(IEnumerator body, string name)
        {
            while (true)
            {
                object current;
                try
                {
                    if (!body.MoveNext()) yield break;
                    current = body.Current;
                }
                catch (Exception ex)
                {
                    var inner = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    Check(name + "/excecao", false, inner.ToString().Replace('\n', ' '));
                    yield break;
                }
                yield return current;
            }
        }

        private GameObject _npc;

        private IEnumerator Api()
        {
            Check("api/montarias-carregado", MontApi != null);
            Check("api/versao-1", MontApi != null && Equals(MontApi.GetField("Version")?.GetValue(null), 1));
            var ids = (string[])Static(MontApi, "MountIds");
            Check("api/tem-capivara", ids.Contains("capivara"), string.Join(",", ids));
            Check("api/nome-capivara", (string)Static(MontApi, "MountName", "capivara") == "Capivara",
                (string)Static(MontApi, "MountName", "capivara"));

            // O que o painel do Mestre pergunta antes de mostrar "O mod Montarias nao esta instalado."
            // Resolve olha no maximo a cada 10 s: espera ate 15.
            yield return Until(() => (bool)Static(NpcMontApi, "IsAvailable"), 15f);
            Check("api/npcvalheim-acha-o-montarias", (bool)Static(NpcMontApi, "IsAvailable"));

            var key = Static(MontSettings, "MenuKey") as BepInEx.Configuration.ConfigEntry<BepInEx.Configuration.KeyboardShortcut>;
            Check("api/tecla-menu-U", key != null && key.Value.MainKey == KeyCode.U, key?.Value.ToString());

            Static(MontApi, "RequestLocalState");
            yield return Until(() => (bool)Static(MontApi, "LocalKnown"), 15f);
            Check("api/ficha-do-servidor", (bool)Static(MontApi, "LocalKnown"), $"nivel={Rank} capivara={OwnsJavali}");
            Check("api/comeca-sem-nada", Rank == 0 && !OwnsJavali);
        }

        private IEnumerator Mestre()
        {
            var prefab = ZNetScene.instance.GetPrefab("NpcValheim_MountTrainer");
            Check("mestre/prefab", prefab != null);
            if (prefab == null) yield break;

            Vector3 at = Me.transform.position + Me.transform.forward * 2.5f;
            _npc = Instantiate(prefab, at, Quaternion.LookRotation(-Me.transform.forward));
            yield return Until(() => _npc.GetComponent(TrainerNpc) != null && _npc.GetComponent<ZNetView>().IsValid(), 10f);
            yield return new WaitForSeconds(2f);
            Check("mestre/no-mundo", _npc.GetComponent(TrainerNpc) != null);

            var offers = (IList)Static(Catalog, "Current");
            Check("mestre/ofertas", offers.Count >= 4, $"n={offers.Count}");

            var hoverable = _npc.GetComponent(TrainerNpc) as Interactable;
            Check("mestre/abre-o-painel", hoverable != null && hoverable.Interact(Me, false, false));
            yield return new WaitForSeconds(3f);
            var texts = VisibleTexts();
            Check("mestre/sem-aviso-de-nao-instalado", !texts.Any(t => t.Contains("não está instalado")),
                string.Join(" | ", texts.Where(t => t.Contains("instalad"))));
            Check("mestre/aba-habilidade", texts.Any(t => t.IndexOf("Habilidade de Montaria", StringComparison.OrdinalIgnoreCase) >= 0));
            yield return Photo("mestre-treinamento");
        }

        private object Offer(string kind, string id)
        {
            foreach (var o in (IList)Static(Catalog, "Current"))
                if (Get(o, "Kind").ToString() == kind && (string)Get(o, "Id") == id) return o;
            return null;
        }

        private IEnumerator Buy(string name, object offer, Func<bool> granted)
        {
            Check(name + "/oferta", offer != null);
            if (offer == null) yield break;
            Static(Trainer, "EndPurchase");
            Static(Trainer, "TryBeginPurchase");
            bool sent = (bool)Static(Trainer, "RequestBuy", _npc.GetComponent(TrainerNpc), offer);
            Check(name + "/pedido-enviado", sent);
            yield return Until(granted, 20f);
            Check(name + "/concedido", granted(), $"nivel={Rank} capivara={OwnsJavali} msg={Static(Trainer, "LastMessage")}");
        }

        private IEnumerator Compra()
        {
            if (_npc == null) { Check("compra/sem-npc", false); yield break; }
            var coins = ObjectDB.instance.GetItemPrefab("Coins");
            Me.GetInventory().AddItem(coins, 500);
            int before = CoinsInBag();

            yield return Buy("compra/habilidade-1", Offer("Skill", "1"), () => Rank == 1);
            yield return Until(() => CoinsInBag() == before - 500, 5f);
            Check("compra/habilidade-1-cobrou-coins", CoinsInBag() == before - 500, $"antes={before} depois={CoinsInBag()}");

            // Deadcoins: o run-e2e.ps1 deixou 1000 no arquivo do servidor.
            yield return Buy("compra/capivara", Offer("Mount", "capivara"), () => OwnsJavali);
            yield return Until(() => MountItem() != null, 10f);
            Check("compra/item-da-capivara-na-bolsa", MountItem() != null);

            // Comprar de novo e recusado.
            Func<string> said = () => (string)Static(Trainer, "LastMessage") ?? "";
            yield return Buy("compra/capivara-de-novo", Offer("Mount", "capivara"), () => said().Contains("já possui"));

            yield return Photo("depois-da-compra");
            var root = T("NpcValheim", "NpcValheim.UI.UiRoot");
            var ui = root?.GetField("_instance", Any)?.GetValue(null);
            root?.GetMethod("Close", Any)?.Invoke(ui, root.GetMethod("Close", Any).GetParameters().Length == 0 ? null : new object[] { null });
            yield return new WaitForSeconds(1f);
            Check("compra/painel-fechado", !(bool)Static(root, "IsOpen"));
        }

        private IEnumerator UseMountItem()
        {
            var item = MountItem();
            if (item == null) yield break;
            Me.UseItem(Me.GetInventory(), item, true);
            yield return null;
        }

        private IEnumerator Montar()
        {
            if (MountItem() == null) { Check("montar/sem-item", false); yield break; }
            // Longe do NPC e dos outros, com espaco.
            yield return UseMountItem();
            yield return Until(() => Riding, 15f);
            Check("montar/usou-o-item-e-montou", Riding);
            var mount = Static(Javali, "FindOwned", Me) as GameObject;
            Check("montar/capivara-existe", mount != null, mount != null ? mount.name : "");
            yield return new WaitForSeconds(1f);
            yield return Photo("montado");
            CheckAiLoop("montar/ia-sem-excecao-montado");
            yield return RunSafely(SaddleRotation(mount), "sela");

            Vector3 mountAt = mount != null ? mount.transform.position : Me.transform.position;
            yield return UseMountItem();
            yield return Until(() => !Riding, 10f);
            Check("montar/usou-de-novo-e-desceu", !Riding);
            CheckLanding("montar/desceu-ao-lado", mountAt);
            yield return new WaitForSeconds(1.5f);
            Check("montar/capivara-sumiu", Static(Javali, "FindOwned", Me) as GameObject == null);
            CheckAiLoop("montar/ia-sem-excecao-depois-de-descer");

            // De novo: o bug do 0.3.15 so aparecia na segunda montaria, com a primeira
            // destruida presa na lista de IA do jogo.
            yield return UseMountItem();
            yield return Until(() => Riding, 15f);
            Check("montar/montou-de-novo", Riding);
            mount = Static(Javali, "FindOwned", Me) as GameObject;
            yield return new WaitForSeconds(1f);
            CheckAiLoop("montar/ia-sem-excecao-segunda-vez");
            mountAt = mount != null ? mount.transform.position : Me.transform.position;
            yield return UseMountItem();
            yield return Until(() => !Riding, 10f);
            Check("montar/desceu-de-novo", !Riding);
            CheckLanding("montar/desceu-ao-lado-de-novo", mountAt);
            yield return new WaitForSeconds(1.5f);
            CheckAiLoop("montar/ia-sem-excecao-no-fim");
        }

        private static Transform SaddleOf(GameObject mount) =>
            mount != null
                ? mount.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MountSaddleVisual")
                : null;

        /// <summary>SelaRotacaoX/Y/Z mudados com a montaria na tela: a sela tem que girar na
        /// hora, sem invocar de novo. Volta tudo a 0 no fim.</summary>
        private IEnumerator SaddleRotation(GameObject mount)
        {
            var profile = Static(MontSettings, "Find", mount);
            Check("sela/perfil", profile != null);
            if (profile == null) yield break;
            var x = Get(profile, "SaddleXRotation") as BepInEx.Configuration.ConfigEntry<float>;
            var y = Get(profile, "SaddleYRotation") as BepInEx.Configuration.ConfigEntry<float>;
            var z = Get(profile, "SaddleZRotation") as BepInEx.Configuration.ConfigEntry<float>;
            Check("sela/configs-existem", x != null && y != null && z != null);
            if (x == null || y == null || z == null) yield break;

            var saddle = SaddleOf(mount);
            Check("sela/existe", saddle != null);
            if (saddle == null) yield break;
            var before = saddle.localRotation;
            Log($"sela antes: local={before.eulerAngles} mundo={saddle.rotation.eulerAngles} config=({x.Value},{y.Value},{z.Value})");

            y.Value = 90f;
            yield return new WaitForSeconds(1f);
            saddle = SaddleOf(mount);
            float turned = saddle != null ? Quaternion.Angle(before, saddle.localRotation) : -1f;
            Check("sela/rotacao-y-90-gira", saddle != null && Mathf.Abs(turned - 90f) < 2f,
                saddle != null ? $"girou={turned:0.0} local={saddle.localRotation.eulerAngles}" : "sela sumiu");
            yield return Photo("sela-y90");

            x.Value = 20f;
            yield return new WaitForSeconds(1f);
            saddle = SaddleOf(mount);
            var want = Quaternion.Euler(20f, 90f, 0f);
            Check("sela/rotacao-x-20-y-90", saddle != null && Quaternion.Angle(saddle.localRotation, before * want) < 2f,
                saddle != null ? $"local={saddle.localRotation.eulerAngles}" : "sela sumiu");

            x.Value = 0f;
            y.Value = 0f;
            z.Value = 0f;
            yield return new WaitForSeconds(1f);
        }

        /// <summary>MonoUpdaters esvazia a lista de IA so no fim do loop; se sobrou algo, uma
        /// IA lancou excecao e o FixedUpdate de todos os personagens parou ali.</summary>
        private void CheckAiLoop(string name)
        {
            var type = T("assembly_valheim", "MonoUpdaters");
            var updaters = type != null ? UnityEngine.Object.FindObjectOfType(type) : null;
            var list = updaters != null ? Get(updaters, "m_ai") as System.Collections.IList : null;
            Check(name, list != null && list.Count == 0, list != null ? $"pendentes={list.Count}" : "sem MonoUpdaters.m_ai");
        }

        private void CheckLanding(string name, Vector3 mountAt)
        {
            var pos = Me.transform.position;
            var flat = pos - mountAt;
            flat.y = 0f;
            float ground = ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h, 1000) ? h : pos.y;
            Check(name, flat.magnitude < 6f && pos.y > ground - 0.5f,
                $"jogador={pos} montaria={mountAt} chao={ground:0.00}");
        }

        private IEnumerator Combate()
        {
            // Bit de combate do Deadheim no ZDO do jogador (dh_pvpFlags & 16): mesmo com o buff
            // desligado, o Montarias tem que recusar.
            var zdo = Me.GetComponent<ZNetView>().GetZDO();
            int key = "dh_pvpFlags".GetStableHashCode();
            int old = zdo.GetInt(key, 0);
            zdo.Set(key, old | 16);
            Check("combate/flag-do-deadheim-conta", (bool)Static(Combat, "IsInCombat", Me));
            zdo.Set(key, old);
            Check("combate/sem-flag-livre", !(bool)Static(Combat, "IsInCombat", Me));

            // Buff de combate do Deadheim: montar e recusado.
            int dh = "DH_Combat".GetStableHashCode();
            var se = ObjectDB.instance.GetStatusEffect(dh);
            Check("combate/buff-do-deadheim-existe", se != null);
            if (se == null) yield break;
            Me.GetSEMan().AddStatusEffect(dh, true);
            yield return new WaitForSeconds(0.5f);
            yield return UseMountItem();
            yield return new WaitForSeconds(5f);
            Check("combate/em-combate-nao-monta", !Riding);
            Me.GetSEMan().RemoveStatusEffect(dh, true);
            yield return new WaitForSeconds(0.5f);
            yield return UseMountItem();
            yield return Until(() => Riding, 15f);
            Check("combate/fora-de-combate-monta", Riding);
            if (Riding)
            {
                yield return UseMountItem();
                yield return Until(() => !Riding, 10f);
            }
        }
    }
}
