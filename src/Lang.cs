namespace HideAndSeekMonster
{
    internal static class L
    {
        private static bool De => Plugin.Language.Value == UiLanguage.Deutsch;

        public static string MenuTitle => "Hide & Seek – Monster";
        public static string HostOnly => De ? "Nur der Host kann eine Runde starten." : "Only the host can start a round.";
        public static string OnlyInLevel => De ? "Runden können nur in einem Level gestartet werden." : "Rounds can only be started inside a level.";
        public static string ChooseSeeker => De ? "Wer ist der Seeker (Monster)?" : "Who is the seeker (monster)?";
        public static string Random => De ? "Zufällig" : "Random";
        public static string HideTime => De ? "Versteckzeit" : "Hide time";
        public static string RoundTime => De ? "Suchzeit" : "Hunt time";
        public static string KillRange => De ? "Fangreichweite" : "Kill range";
        public static string SeekerSpeed => De ? "Monster-Tempo" : "Monster speed";
        public static string DisableEnemies => De ? "Normale Gegner deaktivieren" : "Disable normal enemies";
        public static string Invincible => De ? "Monster unverwundbar" : "Monster invincible";
        public static string Blind => De ? "Monster beim Verstecken blind" : "Blind monster while hiding";
        public static string Start => De ? "Runde starten" : "Start round";
        public static string Abort => De ? "Runde abbrechen" : "Abort round";
        public static string Close => De ? "Schließen" : "Close";
        public static string NotEnoughPlayers => De ? "Mindestens 2 lebende Spieler nötig." : "At least 2 living players are needed.";
        public static string RoundRunning => De ? "Eine Runde läuft gerade." : "A round is currently running.";

        public static string YouAreSeeker => De ? "DU BIST DAS MONSTER" : "YOU ARE THE MONSTER";
        public static string WaitForHiders(int s) => De ? $"Die anderen verstecken sich... {s}s" : $"The others are hiding... {s}s";
        public static string HideNow(string seeker, int s) => De ? $"VERSTECK DICH! {seeker} ist das Monster – {s}s" : $"HIDE! {seeker} is the monster – {s}s";
        public static string HuntStarted => De ? "Das Monster ist los!" : "The monster is loose!";
        public static string Hunt(int s, int left) => De ? $"Jagd: {s}s | Übrige Hider: {left}" : $"Hunt: {s}s | Hiders left: {left}";
        public static string Hiding(int s) => De ? $"Verstecken: {s}s" : $"Hiding: {s}s";
        public static string Caught(string name) => De ? $"{name} wurde gefressen!" : $"{name} got eaten!";
        public static string SeekerWins(string name) => De ? $"{name} (Monster) hat gewonnen!" : $"{name} (monster) wins!";
        public static string HidersWin => De ? "Die Hider haben überlebt und gewonnen!" : "The hiders survived and win!";
        public static string Aborted => De ? "Runde abgebrochen." : "Round aborted.";
        public static string MenuHint(string key) => De ? $"[{key}] Hide & Seek Menü" : $"[{key}] Hide & Seek menu";
    }
}
