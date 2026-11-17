using System;
using System.Globalization;

namespace ValheimMontarias
{
    /// <summary>
    /// Textos PT/EN do menu de montarias. Segue o idioma do jogo (Portuguese* = pt, resto = en);
    /// sem Localization (servidor dedicado) usa pt. Uso: L.T("Texto pt", "English text", args...).
    /// </summary>
    internal static class L
    {
        internal static bool Portugues
        {
            get
            {
                var loc = Localization.instance;
                if (loc == null) return true;
                string lang = loc.GetSelectedLanguage() ?? "";
                return lang.StartsWith("Portuguese", StringComparison.OrdinalIgnoreCase);
            }
        }

        // Mensagens que o servidor manda ja prontas em portugues: o cliente traduz aqui, no idioma dele.
        private static readonly (System.Text.RegularExpressions.Regex rx, string en)[] ServerMsgs =
        {
            (R("Apenas administradores."), "Admins only."),
            (R("O servidor não conseguiu identificar a sua conta."), "The server could not identify your account."),
            (R("Sua ficha de montarias está ilegível no servidor. Fale com um admin."), "Your mount record is unreadable on the server. Talk to an admin."),
            (R("Resgate de montarias desativado neste servidor."), "Mount redeeming is disabled on this server."),
            (R("Esta montaria não pode ser resgatada."), "This mount cannot be redeemed."),
            (R("(.+) desbloqueada! Ela está na lista de montarias."), "$1 unlocked! It is in your mount list."),
            (R("(.+) liberada para você."), "$1 unlocked for you."),
            (R("Você já tem o nível máximo da habilidade."), "You already have the maximum skill level."),
            (R("Você aprendeu (.+)."), "You learned $1."),
            (R("Habilidade e montarias zeradas."), "Skill and mounts reset."),
            (R("Skin de montaria desativada neste servidor."), "Mount skins are disabled on this server."),
            (R("Nome de montaria desativado neste servidor."), "Mount names are disabled on this server."),
            (R("Este recurso é exclusivo para VIP (Aesir)."), "This feature is VIP (Aesir) only."),
            (R("Skin de (.+) removida."), "$1 skin removed."),
            (R("Skin de (.+) definida: (.+)."), "$1 skin set: $2."),
            (R("Nome de (.+) removido."), "$1 name removed."),
            (R("Nome de (.+) definido: (.+)."), "$1 name set: $2."),
            (R("Este nível de habilidade não existe."), "This skill level does not exist."),
            (R("Você já sabe (.+)."), "You already know $1."),
            (R("Você já possui (.+)."), "You already own $1."),
            (R("Esta montaria não existe."), "This mount does not exist."),
            (R("Você não possui (.+)."), "You do not own $1."),
            (R("Esta skin não existe."), "This skin does not exist."),
            (R("Só o servidor pode fazer isso."), "Only the server can do that."),
            (R("Falha ao gravar no servidor. Nada mudou."), "Failed to save on the server. Nothing changed."),
        };

        private static System.Text.RegularExpressions.Regex R(string pt) =>
            new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(pt).Replace(@"\(\.\+\)", "(.+)") + "$",
                System.Text.RegularExpressions.RegexOptions.Singleline);

        /// <summary>Traduz uma mensagem do servidor (pt) para o idioma do jogo; sem correspondencia, devolve igual.</summary>
        internal static string Server(string msg)
        {
            if (Portugues || string.IsNullOrEmpty(msg)) return msg;
            foreach (var (rx, en) in ServerMsgs)
            {
                var m = rx.Match(msg);
                if (m.Success) return RidingRanks.DisplayAll(rx.Replace(msg, en));
            }
            return msg;
        }

        internal static string T(string pt, string en, params object[] args)
        {
            string txt = Portugues ? pt : en;
            if (args == null || args.Length == 0) return txt;
            try { return string.Format(CultureInfo.InvariantCulture, txt, args); }
            catch (FormatException) { return txt; }
        }
    }
}
