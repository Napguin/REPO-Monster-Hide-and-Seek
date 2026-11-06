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
        public static string ChooseMonster => De ? "Welches Monster?" : "Which monster?";
        public static string NoMonsters => De ? "(Keine Monster gefunden – eigenes Monster wird benutzt)" : "(No monsters found – custom monster is used)";
        public static string AttackRange => De ? "Angriffsreichweite" : "Attack range";
        public static string MonsterSize => De ? "Monstergröße" : "Monster size";
        public static string TestMode => De ? "Testmodus (alleine testen)" : "Test mode (solo testing)";
        public static string TestModeHint => De ? "Du wirst das Monster, Trainingspuppen sind die Hider." : "You become the monster, training dummies are the hiders.";
        public static string TestModeTitle => De ? "=== TESTMODUS ===" : "=== TEST MODE ===";
        public static string TestKeys => De
            ? "F9 = nächstes Monster (Shift+F9 = vorheriges)\nF10 = Puppe vor dir spawnen\nF11 = diese Anzeige ein/aus\nF8 = Menü (Runde abbrechen)"
            : "F9 = next monster (Shift+F9 = previous)\nF10 = spawn dummy in front of you\nF11 = toggle this overlay\nF8 = menu (abort round)";
        public static string AttackHit(string name, float dist) => De ? $"TREFFER {name} ({dist:0.0}m)" : $"HIT {name} ({dist:0.0}m)";
        public static string AttackMiss(string name, float dist, float angle, float range) => De
            ? $"verfehlt – nächste Puppe {name}: {dist:0.0}m (Reichweite {range:0.0}m), Winkel {angle:0}° (max ~70°)"
            : $"missed – nearest {name}: {dist:0.0}m (range {range:0.0}m), angle {angle:0}° (max ~70°)";
        public static string AttackMissNoTarget => De ? "verfehlt – keine Puppen mehr" : "missed – no dummies left";
        public static string RoarBanner => De ? "BRÜLLEN!" : "ROAR!";
        public static string NoSpecial => De ? "Dieses Monster hat keine Spezialfähigkeit." : "This monster has no special ability.";
        public static string LaserControl(string key, string cd) => De ? $"   [{key}] Laser{cd}" : $"   [{key}] Laser{cd}";
        public static string LaserHit(string name) => De ? $"LASER traf {name}" : $"LASER hit {name}";
        public static string SeekerChooses => De ? "Seeker wählt sein Monster selbst" : "Seeker picks their own monster";
        public static string ChooseYourMonster => De ? "Wähle dein Monster" : "Choose your monster";
        public static string ChoiceHint(int s) => De ? $"Die anderen verstecken sich noch {s}s – such dir aus, was du sein willst:" : $"The others are hiding for {s}s – pick what you want to be:";
        public static string Current(string name) => De ? $"Aktuell: {name}" : $"Current: {name}";
        public static string Done => De ? "Fertig" : "Done";
        public static string ScoreTitle => De ? "Punkte" : "Scores";
        public static string NoScores => De ? "(noch keine Runde gewertet)" : "(no scored round yet)";
        public static string ResetScores => De ? "Punkte zurücksetzen" : "Reset scores";
        public static string SeekerWord => De ? "Monster" : "monster";
        public static string MonsterDied(string name) => De ? $"Das Monster ({name}) ist tot – die Hider gewinnen!" : $"The monster ({name}) is dead – the hiders win!";
        public static string ReopenChoice => De ? "[C] Monster ändern" : "[C] change monster";
        public static string SpecialLaser => De ? "Spezial: Laser" : "Special: laser";
        public static string BasicAbilities => De ? "Angriff · Brüllen" : "Attack · roar";
        public static string YouAre => De ? "DU BIST" : "YOU ARE";
        public static string Abilities => De ? "Fähigkeiten" : "Abilities";
        public static string AbilityAttack => De ? "Angreifen (Sprung nach vorn, tötet Hider vor dir)" : "Attack (lunge, kills hiders in front of you)";
        public static string AbilityRoar => De ? "Echo-Brüllen (zeigt die Richtung von Hidern in 15 m)" : "Echo roar (shows the direction of hiders within 15 m)";
        public static string AbilityLaser => De ? "Laser (tötet alles im Strahl)" : "Laser (kills everything in the beam)";
        public static string AbilityView => De ? "Kamera wechseln" : "Switch camera";
        public static string NoSpecialShort => De ? "keine Spezialfähigkeit" : "no special ability";
        public static string ChoiceInfo => De ? "Die anderen sehen dich sofort als dieses Monster. Du kannst wechseln, bis die Jagd beginnt. Berühren tötet ebenfalls." : "The others see you as this monster right away. You can switch until the hunt starts. Touching kills as well.";
        public static string RandomMonster => De ? "Zufälliges Monster" : "Random monster";
        public static string AbilityName(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Laser: return "Laser";
                case AbilityType.Shot: return De ? "Schuss" : "Shot";
                case AbilityType.Sprint: return "Sprint";
                case AbilityType.Leap: return De ? "Sprungangriff" : "Leap attack";
                case AbilityType.Reveal: return De ? "Röntgenblick" : "X-ray vision";
                case AbilityType.Throw: return De ? "Wurf" : "Throw";
                case AbilityType.Shockwave: return De ? "Schockwelle" : "Shockwave";
                default: return "-";
            }
        }

        public static string AbilityDescription(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Laser: return De ? "Feuert 3 s einen Laser. Alles im Strahl stirbt, Wände blocken ihn." : "Fires a laser for 3 s. Everything in the beam dies, walls block it.";
                case AbilityType.Shot: return De ? "Ein Schuss auf große Entfernung. Tötet den ersten Hider in der Schusslinie." : "One long-range shot. Kills the first hider in the line of fire.";
                case AbilityType.Sprint: return De ? "3 s extrem schnell." : "Extremely fast for 3 s.";
                case AbilityType.Leap: return De ? "Weiter Sprung nach vorn. Wer dabei berührt wird, stirbt." : "Big jump forward. Anyone touched during the jump dies.";
                case AbilityType.Reveal: return De ? "5 s lang siehst du alle Hider durch Wände." : "See all hiders through walls for 5 s.";
                case AbilityType.Throw: return De ? "Wirft ein Geschoss im Bogen. Tötet beim Treffer." : "Throws a projectile in an arc. Kills on hit.";
                case AbilityType.Shockwave: return De ? "Hider in 10 m Umkreis werden 2,5 s stark verlangsamt." : "Hiders within 10 m are heavily slowed for 2.5 s.";
                default: return "";
            }
        }

        public static string AbilityUsedByMonster(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Sprint: return De ? "Das Monster sprintet!" : "The monster is sprinting!";
                case AbilityType.Reveal: return De ? "Das Monster sieht dich durch Wände!" : "The monster sees you through walls!";
                case AbilityType.Shockwave: return De ? "SCHOCKWELLE!" : "SHOCKWAVE!";
                case AbilityType.Leap: return De ? "Das Monster springt!" : "The monster leaps!";
                default: return "";
            }
        }

        public static string Stunned => De ? "Betäubt!" : "Stunned!";
        public static string Ready => De ? "bereit" : "ready";
        public static string Special => De ? "Spezial" : "Special";
        public static string Attack => De ? "Angriff" : "Attack";
        public static string Roar => De ? "Echo" : "Echo";
        public static string EchoFound(int n) => De ? $"ECHO: {n} Hider in der Nähe" : $"ECHO: {n} hider(s) nearby";
        public static string EchoNothing => De ? "ECHO: niemand in der Nähe" : "ECHO: nobody nearby";
        public static string Echoed => De ? "GEORTET! Das Monster weiß, in welcher Richtung du bist." : "ECHOED! The monster knows which direction you're in.";
        public static string Camera => De ? "Kamera" : "Camera";
        public static string MonsterSpecialInfo(string monster, string special) => De ? $"{monster} – Spezial: {special}" : $"{monster} – special: {special}";

        public static string ChooseSeekerShort => De ? "Seeker" : "Seeker";
        public static string You => De ? "du" : "you";
        public static string StartMonsterHint => De ? "Der Seeker sucht sich sein Monster in der Versteckphase aus. Hier wählst du nur, womit er startet." : "The seeker picks their monster during the hiding phase. This only sets the starting monster.";
        public static string FixedMonsterHint => De ? "Der Seeker wird genau dieses Monster." : "The seeker becomes exactly this monster.";
        public static string RandomMonsterHint => De ? "Überraschung" : "Surprise";
        public static string Rules => De ? "Regeln" : "Rules";
        public static string CanGrab => De ? "Monster kann greifen (Türen öffnen)" : "Monster can grab (open doors)";
        public static string On => De ? "AN" : "ON";
        public static string Off => De ? "AUS" : "OFF";
        public static string Start => De ? "Runde starten" : "Start round";
        public static string Abort => De ? "Runde abbrechen" : "Abort round";
        public static string Close => De ? "Schließen" : "Close";
        public static string NotEnoughPlayers => De ? "Mindestens 2 lebende Spieler nötig." : "At least 2 living players are needed.";
        public static string RoundRunning => De ? "Eine Runde läuft gerade." : "A round is currently running.";

        public static string YouAreSeeker => De ? "DU BIST DAS MONSTER" : "YOU ARE THE MONSTER";
        public static string WaitForHiders(int s) => De ? $"Die anderen verstecken sich... {s}s" : $"The others are hiding... {s}s";
        public static string HideNow(string seeker, string monster, int s) => De ? $"VERSTECK DICH! {seeker} ist {monster} – {s}s" : $"HIDE! {seeker} is the {monster} – {s}s";
        public static string SeekerControls(string atk, string atkCd, string roar, string roarCd, string view) =>
            De ? $"[{atk}] Angreifen{atkCd}   [{roar}] Brüllen{roarCd}   [{view}] Kamera"
               : $"[{atk}] Attack{atkCd}   [{roar}] Roar{roarCd}   [{view}] Camera";
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
