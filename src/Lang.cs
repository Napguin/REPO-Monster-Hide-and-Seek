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
        public static string TestModeTitle => (De ? "=== TESTMODUS ===" : "=== TEST MODE ===") + $"   v{Plugin.Version}";
        public static string TestKeys => De
            ? "Keine Cooldowns im Testmodus\nF9 = nächstes Monster (Shift+F9 = vorheriges)\nF10 = Puppe vor dir spawnen\nF11 = diese Anzeige ein/aus\nF8 = Menü (Runde abbrechen)"
            : "No cooldowns in test mode\nF9 = next monster (Shift+F9 = previous)\nF10 = spawn dummy in front of you\nF11 = toggle this overlay\nF8 = menu (abort round)";
        public static string AttackHit(string name, float dist) => De ? $"TREFFER {name} ({dist:0.0}m)" : $"HIT {name} ({dist:0.0}m)";
        public static string AttackMiss(string name, float dist, float angle, float range) => De
            ? $"verfehlt – nächste Puppe {name}: {dist:0.0}m (Reichweite {range:0.0}m), Winkel {angle:0}° (max ~70°)"
            : $"missed – nearest {name}: {dist:0.0}m (range {range:0.0}m), angle {angle:0}° (max ~70°)";
        public static string AttackMissNoTarget => De ? "verfehlt – keine Puppen mehr" : "missed – no dummies left";
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
        public static string BasicAbilities => De ? "Angriff · Player Scan" : "Attack · player scan";
        public static string YouAre => De ? "DU BIST" : "YOU ARE";
        public static string Abilities => De ? "Fähigkeiten" : "Abilities";
        public static string AbilityAttack => De ? $"Angreifen (Sprung nach vorn, {Plugin.AttackDamage.Value} Schaden)" : $"Attack (lunge, {Plugin.AttackDamage.Value} damage)";
        public static string AbilityRoar => De ? "Player Scan (zeigt, wie viele Spieler in 15 m sind)" : "Player scan (shows how many players are within 15 m)";
        public static string AbilityLaser => De ? "Laser (tötet alles im Strahl)" : "Laser (kills everything in the beam)";
        public static string AbilityView => De ? "Kamera wechseln" : "Switch camera";
        public static string NoSpecialShort => De ? "keine Spezialfähigkeit" : "no special ability";
        public static string ChoiceInfo => De ? "Die anderen sehen dich sofort als dieses Monster. Du kannst wechseln, bis die Jagd beginnt. Reinlaufen tötet nicht – du musst angreifen." : "The others see you as this monster right away. You can switch until the hunt starts. Running into hiders does nothing – you have to attack.";
        public static string RandomMonster => De ? "Zufälliges Monster" : "Random monster";
        public static string AbilityName(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Laser: return "Laser";
                case AbilityType.Shot: return De ? "Schuss" : "Shot";
                case AbilityType.Sprint: return "Rapid Charge";
                case AbilityType.FlyingCharge: return "Flying Charge";
                case AbilityType.Bomb: return De ? "Kopfbombe" : "Head bomb";
                case AbilityType.Puke: return De ? "Kotzen" : "Puke";
                case AbilityType.Levitate: return De ? "Levitation" : "Levitate";
                case AbilityType.Slam: return De ? "Bodenschlag" : "Ground slam";
                case AbilityType.Pull: return De ? "Herzgas" : "Heart gas";
                case AbilityType.Gamble: return De ? "Glücksspiel" : "Gamble";
                case AbilityType.FlyingGrab: return "Flying Grab";
                case AbilityType.ZeroGGrab: return "Zero-G Grab";
                case AbilityType.NoxiousGas: return "Noxious Gas";
                case AbilityType.Swarm: return De ? "Schwarm" : "Swarm";
                case AbilityType.Charge: return "Charge";
                case AbilityType.Lunge: return "Lunge";
                case AbilityType.ItemThrow: return De ? "Gegenstand werfen" : "Item Throw";
                case AbilityType.Tornado: return "Tornado";
                case AbilityType.Launch: return De ? "Wegschleudern" : "Launch";
                case AbilityType.Impulse: return "Impulse";
                case AbilityType.Lasso: return "Lasso Grab";
                case AbilityType.TumbleBash: return "Tumble-Bash";
                case AbilityType.BalloonMines: return De ? "Ballon-Minen" : "Balloon Mines";
                case AbilityType.Leap: return De ? "Sprungangriff" : "Leap attack";
                case AbilityType.Reveal: return "Radar Ping";
                case AbilityType.Throw: return De ? "Wurf" : "Throw";
                case AbilityType.Shockwave: return De ? "Schockwelle" : "Shockwave";
                default: return "-";
            }
        }

        public static string AbilityDescription(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Laser: return De ? "Feuert 3 s einen Laser: 50 Schaden pro Sekunde im Strahl. Wände blocken ihn." : "Fires a laser for 3 s: 50 damage per second in the beam. Walls block it.";
                case AbilityType.Shot: return De ? $"Ein Schuss auf große Entfernung: {HideAndSeekMonster.Abilities.ShotDamage} Schaden beim ersten Hider in der Schusslinie." : $"One long-range shot: {HideAndSeekMonster.Abilities.ShotDamage} damage to the first hider in the line of fire.";
                case AbilityType.Sprint: return De ? $"5 s 1,5-mal so schnell, {HideAndSeekMonster.Abilities.RapidChargeDamage} Schaden alle 0,5 s an Hidern in der Nähe. Kein normaler Angriff währenddessen." : $"1.5x speed for 5 s, {HideAndSeekMonster.Abilities.RapidChargeDamage} damage every 0.5 s to hiders close to you. No main attack meanwhile.";
                case AbilityType.FlyingCharge: return De ? $"6 s fliegen (Springen = hoch, Ducken = runter), {HideAndSeekMonster.Abilities.FlyingChargeDamage} Schaden alle 0,5 s an Hidern in der Nähe. Kein normaler Angriff währenddessen." : $"Fly for 6 s (jump = up, crouch = down), {HideAndSeekMonster.Abilities.FlyingChargeDamage} damage every 0.5 s to hiders close to you. No main attack meanwhile.";
                case AbilityType.Leap: return De ? $"Weiter Sprung nach vorn: {HideAndSeekMonster.Abilities.LeapDamage} Schaden an jedem, den du dabei triffst." : $"Big jump forward: {HideAndSeekMonster.Abilities.LeapDamage} damage to anyone you hit.";
                case AbilityType.Reveal: return De ? "Radar-Ping: zeigt 2 s lang Position und Entfernung aller Hider in 30 m durch Wände. 2 min Cooldown. Die Hider werden gewarnt." : "Radar ping: shows position and distance of all hiders within 30 m through walls for 2 s. 2 min cooldown. Hiders get warned.";
                case AbilityType.Throw: return De ? $"Wirft ein Geschoss im Bogen: {HideAndSeekMonster.Abilities.ThrowDamage} Schaden beim Treffer." : $"Throws a projectile in an arc: {HideAndSeekMonster.Abilities.ThrowDamage} damage on hit.";
                case AbilityType.Shockwave: return De ? "Schrei: Hider in 10 m werden weggestoßen und 2,5 s stark verlangsamt." : "Scream: hiders within 10 m are pushed away and heavily slowed for 2.5 s.";
                case AbilityType.Bomb: return De ? "Wirft den eigenen Kopf, der von Wänden abprallt und nach 4 s explodiert (50 Schaden, 4 m). Danach 5 s Uranwolke mit 10 Schaden alle 0,5 s." : "Throws its own head – it bounces off walls and explodes after 4 s (50 damage, 4 m), leaving a uranium cloud (10 damage every 0.5 s for 5 s).";
                case AbilityType.Puke: return De ? "Kotzt 2,5 s nach vorn (7 m): 10 Schaden alle 0,5 s und verlangsamt." : "Pukes forward for 2.5 s (7 m): 10 damage every 0.5 s and slows.";
                case AbilityType.Levitate: return De ? "Hebt alle Hider in 10 m in die Luft und schmettert sie runter: 30 Schaden." : "Lifts every hider within 10 m into the air and slams them down: 30 damage.";
                case AbilityType.Slam: return De ? "Holt aus und schlägt auf den Boden: 50 Schaden in 5 m und Rückstoß." : "Winds up and slams the ground: 50 damage within 5 m and knock-back.";
                case AbilityType.Pull: return De ? "Herzgas: Hider in 12 m werden 3 s lang zum Monster gezogen." : "Heart gas: hiders within 12 m are pulled towards the monster for 3 s.";
                case AbilityType.Gamble: return De ? "Dreht am Rad: eine zufällige Spezialfähigkeit der anderen Monster." : "Spins the wheel: a random special of the other monsters.";
                case AbilityType.Charge: return De ? "Kurz stehen bleiben, dann blitzschnell ein Stück geradeaus nach vorn (nur waagerecht). 60 Schaden bei Kontakt." : "Stops for a moment, then dashes forward very fast over a short distance (horizontal only). 60 damage on contact.";
                case AbilityType.ItemThrow: return De ? "Schnappt sich ein Wertobjekt in der Nähe (4 m). Nochmal drücken: mit voller Wucht werfen – Schaden je nach Tempo und Gewicht." : "Grabs a nearby valuable (4 m). Use again to throw it really fast – damage depends on speed and weight.";
                case AbilityType.Tornado: return De ? "Erzeugt einen kleinen Tornado, der 7 s nach vorn wandert, Gegenstände und Wertobjekte einsaugt und herumwirbelt und am Ende alles wegschleudert – fliegende Teile verletzen Hider. Hider in der Nähe werden angezogen – wer zu nah kommt, wird mitgewirbelt und kann nichts greifen." : "Spawns a small tornado that wanders forward for 7 s, sucks in items and valuables and whirls them around, then flings everything away – flying items hurt hiders. Hiders nearby are pulled in – get too close and you're spun around inside it and can't grab anything.";
                case AbilityType.Launch: return De ? $"Der nächste Hider in {HideAndSeekMonster.Abilities.LaunchRange:0.#} m fällt um und wird in Blickrichtung weggeschleudert ({HideAndSeekMonster.Abilities.LaunchDamage} Schaden)." : $"The closest hider within {HideAndSeekMonster.Abilities.LaunchRange:0.#} m tumbles and gets launched in your camera direction ({HideAndSeekMonster.Abilities.LaunchDamage} damage).";
                case AbilityType.Impulse: return De ? $"Hider in {HideAndSeekMonster.Abilities.ImpulseRange:0} m fallen um und werden zum Monster gesaugt, dann von einer Druckwelle weggeschleudert ({HideAndSeekMonster.Abilities.ImpulseDamage} Schaden)." : $"Hiders within {HideAndSeekMonster.Abilities.ImpulseRange:0} m tumble and get sucked towards you, then blasted away by a shockwave ({HideAndSeekMonster.Abilities.ImpulseDamage} damage).";
                case AbilityType.TumbleBash: return De ? "Rollt 4 s lang schnell herum (mit der Kamera lenken) und prallt sanft von Wänden ab. Rollt sich klein (passt unter Tische). Wer getroffen wird, fällt um und fliegt weg – kein Schaden." : "Rolls around fast for 4 s (steer with the camera), softly bouncing off walls. Curled up small (fits under tables). Anyone hit tumbles and gets knocked away – no damage.";
                case AbilityType.BalloonMines: return De ? "Setzt einen schwebenden Ballon als Mine (5 Stück). Kommt ein Hider nah ran, explodiert er: 40 Schaden. Ein geplatzter Ballon ist nach 3 s wieder da – platzt der letzte hängende, kommen alle erst nach 40 s zurück." : "Places a hovering balloon mine (5 of them). When a hider gets close it explodes: 40 damage. A popped balloon is back after 3 s – but when the last hanging one pops, all come back only after 40 s.";
                case AbilityType.Lasso: return De ? "Wirft ein Lasso in Blickrichtung (18 m). Trifft es einen Hider, wird er 5 s lang gleichmäßig zu dir gezogen – er kann nur noch seitlich ausweichen. Du bist dabei sehr langsam." : "Throws a lasso where you look (18 m). A hit hider is reeled in towards you at a steady speed for 5 s – they can only dodge sideways. You are very slow meanwhile.";
                case AbilityType.Lunge: return De ? "Wie Charge, aber in Blickrichtung – auch nach oben/unten und in der Luft. 60 Schaden bei Kontakt." : "Like Charge, but in the camera direction – also up/down and in the air. 60 damage on contact.";
                case AbilityType.Swarm: return De ? "Ruft 5 echte Gnome/Banger des Spiels, die 30 s lang selbst nach Hidern suchen – sie müssen sie erst sehen. Dich greifen sie nicht an." : "Summons 5 real Gnomes/Bangers of the game that hunt hiders on their own for 30 s – they have to spot them first. They ignore you.";
                case AbilityType.NoxiousGas: return De ? "Stößt 5 große Gaswolken aus, die 10 s liegen bleiben. Hider im Gas werden betäubt: sehr langsam, träge Kamera – jeweils 5 s lang, wer dann noch im Gas steht, bekommt weitere 5 s." : "Spews 5 large gas clouds that linger for 10 s. Hiders in the gas are tranquilized: very slow, sluggish camera – for 5 s; still in the gas after that means another 5 s.";
                case AbilityType.FlyingGrab: return De ? "Unsichtbare Blase (4 m) um dich: Hider darin fallen um und sind 8 s lang betäubt – greif sie dir (Linksklick halten)! Kein normaler Angriff währenddessen." : "Invisible bubble (4 m) around you: hiders inside tumble and are stunned for 8 s – grab them (hold left click)! No main attack meanwhile.";
                case AbilityType.ZeroGGrab: return De ? "Betäubungsblase (6 m) mit Schwerelosigkeit: Hider darin fallen um, schweben und sind 8 s lang betäubt – greif sie dir! Kein normaler Angriff währenddessen." : "Stun bubble (6 m) with zero gravity: hiders inside tumble, float and are stunned for 8 s – grab them! No main attack meanwhile.";
                default: return "";
            }
        }

        public static string AbilityUsedByMonster(AbilityType a)
        {
            switch (a)
            {
                case AbilityType.Sprint: return De ? "RAPID CHARGE! Das Monster stürmt los!" : "RAPID CHARGE! The monster is charging!";
                case AbilityType.FlyingCharge: return De ? "FLYING CHARGE! Das Monster fliegt!" : "FLYING CHARGE! The monster is flying!";
                case AbilityType.Shockwave: return De ? "SCHREI!" : "SCREAM!";
                case AbilityType.Bomb: return De ? "BOMBE! Weg vom Kopf!" : "BOMB! Get away from the head!";
                case AbilityType.Puke: return De ? "Das Monster kotzt!" : "The monster is puking!";
                case AbilityType.Levitate: return De ? "LEVITATION! Das Monster hebt alle hoch!" : "LEVITATE! The monster lifts everyone up!";
                case AbilityType.Slam: return De ? "BODENSCHLAG!" : "GROUND SLAM!";
                case AbilityType.Pull: return De ? "HERZGAS! Du wirst angezogen!" : "HEART GAS! You're being pulled in!";
                case AbilityType.Charge: return De ? "CHARGE! Aus dem Weg!" : "CHARGE! Get out of the way!";
                case AbilityType.Lunge: return De ? "LUNGE! Aus dem Weg!" : "LUNGE! Get out of the way!";
                case AbilityType.Swarm: return De ? "SCHWARM! Da kommen noch mehr!" : "SWARM! Here come more of them!";
                case AbilityType.NoxiousGas: return De ? "NOXIOUS GAS! Raus aus den Wolken!" : "NOXIOUS GAS! Stay out of the clouds!";
                case AbilityType.FlyingGrab: return De ? "FLYING GRAB! Weg vom Monster!" : "FLYING GRAB! Get away from the monster!";
                case AbilityType.ZeroGGrab: return De ? "ZERO-G GRAB! Weg vom Monster!" : "ZERO-G GRAB! Get away from the monster!";
                case AbilityType.Leap: return De ? "Das Monster springt!" : "The monster leaps!";
                case AbilityType.TumbleBash: return De ? "TUMBLE-BASH! Aus dem Weg!" : "TUMBLE-BASH! Get out of the way!";
                case AbilityType.Lasso: return De ? "LASSO! Pass auf!" : "LASSO! Watch out!";
                case AbilityType.Tornado: return De ? "TORNADO! Pass auf fliegende Teile auf!" : "TORNADO! Watch out for flying items!";
                case AbilityType.Impulse: return De ? "IMPULSE! Du wirst eingesaugt!" : "IMPULSE! You're being sucked in!";
                default: return "";
            }
        }

        public static string Gambled(string ability) => De ? $"GLÜCKSSPIEL: {ability}!" : $"GAMBLE: {ability}!";
        public static string GrabCaught(string ability, int n) => De ? $"{ability}: {n} Hider in der Blase" : $"{ability}: {n} hider(s) in the bubble";
        public static string Tranquilized => De ? "Betäubt vom Gas..." : "Tranquilized by the gas...";
        public static string RevealSurvivors(int n) => De ? $"Hier haben sich {n} Hider versteckt!" : $"This is where {n} hider(s) were hiding!";
        public static string Regrouped => De ? "Alle zurück im Truck – gleich wird abgestimmt." : "Everyone back in the truck – voting starts in a moment.";
        public static string VoteTitle => De ? "Nächste Runde" : "Next round";
        public static string VoteWantSeeker => De ? "Ich will das Monster sein" : "I want to be the monster";
        public static string VoteNoVolunteers => De ? "Noch keine Freiwilligen – sonst entscheidet der Zufall." : "No volunteers yet – otherwise it's random.";
        public static string VoteMap => De ? "Map" : "Map";
        public static string VoteStay => De ? "Auf dieser Map bleiben" : "Stay on this map";
        public static string VoteFinish => De ? "Abstimmung beenden" : "Finish vote";
        public static string AfterRoundToggle => De ? "Nach der Runde: wiederbeleben, Truck, Abstimmung" : "After round: revive, truck, vote";
        public static string VoteResult(string seeker, string map)
        {
            string s = string.IsNullOrEmpty(seeker) ? (De ? "Monster: Zufall" : "Monster: random") : (De ? $"Nächstes Monster: {seeker}" : $"Next monster: {seeker}");
            string m = string.IsNullOrEmpty(map) ? (De ? "Map bleibt" : "Same map") : (De ? $"Neue Map: {map}" : $"New map: {map}");
            return s + "  ·  " + m + (De ? "  –  Host startet mit F8" : "  –  host starts with F8");
        }
        public static string TimeRecommendation(int phase, string moon, float hide, float hunt)
        {
            string m = string.IsNullOrEmpty(moon) ? (De ? $"Mondphase {phase}" : $"Moon phase {phase}") : $"{moon} ({phase})";
            string t = $"{(int)(hunt / 60f)}:{(int)(hunt % 60f):00}";
            return De ? $"Empfehlung für {m}: Verstecken {hide:0}s, Jagd {t} min" : $"Recommended for {m}: hide {hide:0}s, hunt {t} min";
        }
        public static string MapSize => De ? "Map-Größe (Level)" : "Map size (level)";
        public static string MapSizeKeep(int cur) => De ? $"aktuell lassen (Level {cur})" : $"keep current (level {cur})";
        public static string MapSizeLevel(int lv) => $"Level {lv}";
        public static string MapSizeHint(int cur, int moon) => De
            ? $"Gerade: Level {cur}, Mondphase {moon}. Gilt für die nächste neue Map (Knopf unten oder Map-Abstimmung). Höheres Level = größere Map."
            : $"Now: level {cur}, moon phase {moon}. Used for the next new map (button below or map vote). Higher level = bigger map.";
        public static string LoadNewMap(int lv) => De ? $"Neue Map laden (Level {lv})" : $"Load new map (level {lv})";
        public static string Apply => De ? "Übernehmen" : "Apply";
        public static string ItemNone => De ? "Kein Wertobjekt in der Nähe!" : "No valuable nearby!";
        public static string ItemPicked => De ? "Geschnappt! Nochmal drücken zum Werfen." : "Grabbed! Press again to throw.";
        public static string BalloonsPlaced(int left, int max) => De ? $"Ballon gesetzt – noch {left}/{max}" : $"Balloon placed – {left}/{max} left";
        public static string AllBalloonsOut => De ? "Alle 5 Ballons hängen – einer muss erst platzen" : "All 5 balloons are out – one has to pop first";
        public static string NoBalloons(float s) => De ? $"Keine Ballons mehr – nächster in {s:0} s" : $"No balloons left – next in {s:0} s";
        public static string Lassoed => De ? "GEFANGEN! Du wirst eingeholt!" : "LASSOED! You're being reeled in!";
        public static string TornadoTrapped => De ? "IM TORNADO GEFANGEN!" : "CAUGHT IN THE TORNADO!";
        public static string LaunchNone => De ? "Kein Hider nah genug!" : "No hider close enough!";
        public static string Launched => De ? "WEGGESCHLEUDERT!" : "LAUNCHED!";
        public static string Stunned => De ? "Betäubt!" : "Stunned!";
        public static string Ready => De ? "bereit" : "ready";
        public static string Special => De ? "Spezial" : "Special";
        public static string Attack => De ? "Angriff" : "Attack";
        public static string Roar => "Scan";
        public static string ScanFound(int n) => De ? $"SCAN: {n} Spieler in der Nähe" : $"SCAN: {n} player(s) nearby";
        public static string ScanNothing => De ? "SCAN: niemand in der Nähe" : "SCAN: nobody nearby";
        public static string Scanned => De ? "GESCANNT! Das Monster weiß, dass jemand in der Nähe ist." : "SCANNED! The monster knows someone is nearby.";
        public static string RadarDetected => De ? "RADAR-PING – DU WURDEST ENTDECKT!" : "RADAR PING – YOU WERE DETECTED!";
        public static string RadarSafe => De ? "Radar-Ping – du wurdest nicht entdeckt." : "Radar ping – you were not detected.";
        public static string RadarFound(int n) => De ? $"RADAR: {n} Hider entdeckt" : $"RADAR: {n} hider(s) detected";
        public static string Damage(string name, int dmg, int hp) => hp > 0 ? (De ? $"{name}  -{dmg}  ({hp} HP übrig)" : $"{name}  -{dmg}  ({hp} HP left)") : (De ? $"{name}  -{dmg}  TOT" : $"{name}  -{dmg}  DEAD");
        public static string NoAttackWhileCharging => De ? "Kein Angriff während des Specials!" : "No attack during the special!";
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
            De ? $"[{atk}] Angreifen{atkCd}   [{roar}] Scan{roarCd}   [{view}] Kamera"
               : $"[{atk}] Attack{atkCd}   [{roar}] Scan{roarCd}   [{view}] Camera";
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
