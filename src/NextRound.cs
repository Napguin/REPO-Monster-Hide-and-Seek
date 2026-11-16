using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// After a round (How To Play): everyone is revived and healed, the monster keeps its monster form, and after
    /// NextRoundSeconds the next round starts by itself (host). At every round start all items with a battery
    /// (weapons, melee durability, equipment) are recharged.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private float _nextRoundAt;          // all PCs (countdown display), host starts the round
        private MonsterVisual _afterMonster; // the monster of the finished round, kept until the next one

        private void ScheduleNextRound()
        {
            float s = Plugin.NextRoundSeconds.Value;
            _nextRoundAt = s > 0f ? Time.time + s : 0f;
        }

        private void KeepMonsterAfterRound()
        {
            if (_afterMonster != null) _afterMonster.Remove();
            _afterMonster = _monster;
            if (_afterMonster != null && _afterMonster.IsLocalMonster) _afterMonster.SetBodyVisible(false); // back in first person
        }

        private void RemoveAfterMonster()
        {
            if (_afterMonster != null) _afterMonster.Remove();
            _afterMonster = null;
        }

        private void NextRoundTick()
        {
            if (RoundActive) { _nextRoundAt = 0f; return; }
            if (_nextRoundAt <= 0f || !IsHost || Time.time < _nextRoundAt || _voteOpen) return;
            if (!InLevel() || !LevelReady()) return; // a voted map is still loading
            ResolveNextSeeker();
            HostStartRound();
            if (RoundActive) { _nextRoundAt = 0f; Plugin.Log.LogInfo("[AfterRound] next round started automatically"); }
            else _nextRoundAt = Time.time + 5f; // not enough players yet – try again
        }

        private static bool LevelReady()
        {
            var lg = LevelGenerator.Instance;
            return lg != null && Compat.Get(lg, "Generated", true);
        }

        private void DrawNextRound(float sw, float u)
        {
            if (RoundActive || _nextRoundAt <= 0f) return;
            int left = Mathf.CeilToInt(_nextRoundAt - Time.time);
            if (left < 0) return;
            var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold, fontSize = Mathf.RoundToInt(22 * u) };
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.Label(new Rect(2 * u, 62 * u, sw, 40 * u), L.NextRoundIn(left), style);
            GUI.color = Color.white;
            GUI.Label(new Rect(0, 60 * u, sw, 40 * u), L.NextRoundIn(left), style);
        }

        /// <summary>Host, at round start: every battery item (guns, melee durability, drones ...) full again.</summary>
        private void HostRechargeAll()
        {
            int n = 0;
            foreach (var b in Object.FindObjectsOfType<ItemBattery>())
            {
                if (b == null) continue;
                if (Compat.Call(b, "SetBatteryLife", 100)) n++;
                else { Compat.Set(b, "batteryLife", 100f); n++; }
            }
            Plugin.Log.LogInfo($"[Round] recharged {n} item(s)");
        }
    }
}
