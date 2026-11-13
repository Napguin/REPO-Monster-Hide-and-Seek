using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// Rugrat – Item Throw: first use picks up the nearest valuable, the next use throws it very fast.
    /// The host moves the item (it owns the map's physics objects), so everybody sees the same thing.
    /// The flying item hurts hiders depending on speed and weight.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private const int MsgItem = 20;
        private const float ItemPickRange = 4f, ItemThrowSpeed = 32f, ItemFlyTime = 2.5f;

        // host
        private Rigidbody _heldItem;
        // items flying after a throw / a tornado fling: each hurts every hider once
        private readonly List<(Rigidbody rb, float until, HashSet<int> hit)> _flying = new List<(Rigidbody, float, HashSet<int>)>();

        private void TrackFlyingItem(Rigidbody rb, float time)
        {
            _flying.RemoveAll(f => f.rb == rb);
            _flying.Add((rb, Time.time + time, new HashSet<int>()));
        }
        // all clients
        private bool _holdingItem;

        private bool LocalSeekerHoldsItem => _holdingItem;

        /// <summary>Host, on every Item Throw press.</summary>
        private void HostItemThrow(Vector3 dir)
        {
            if (!IsHost || Seeker == null) return;
            if (_heldItem == null)
            {
                Vector3 sp = PlayerPos(Seeker) + Vector3.up;
                Rigidbody best = null;
                float bestD = ItemPickRange;
                foreach (var v in Object.FindObjectsOfType<ValuableObject>())
                {
                    if (v == null) continue;
                    var rb = v.GetComponent<Rigidbody>() ?? v.GetComponentInParent<Rigidbody>();
                    if (rb == null || _flying.Any(f => f.rb == rb) || InAnyTornado(rb)) continue;
                    float d = Vector3.Distance(rb.worldCenterOfMass, sp);
                    if (d < bestD) { bestD = d; best = rb; }
                }
                if (best == null) { Send(MsgItem, 0); return; }
                _heldItem = best;
                _heldItem.useGravity = false;
                Plugin.Log.LogInfo($"[Rugrat] picked up '{best.name}' ({best.mass:0.0} kg)");
                var pv = best.GetComponentInParent<Photon.Pun.PhotonView>();
                Send(MsgItem, 1, pv != null ? pv.ViewID : 0);
                return;
            }

            // throw
            var item = _heldItem;
            _heldItem = null;
            item.useGravity = true;
            dir = dir.sqrMagnitude > 0.001f ? dir.normalized : MonsterForward();
            item.velocity = dir * ItemThrowSpeed;
            item.angularVelocity = Random.insideUnitSphere * 10f;
            TrackFlyingItem(item, ItemFlyTime);
            Plugin.Log.LogInfo($"[Rugrat] threw '{item.name}'");
            Send(MsgItem, 2, 0);
        }

        private Vector3 HoldPoint()
        {
            float h = _monster != null ? Mathf.Clamp(_monster.BodyHeight * 0.8f, 0.6f, 2.5f) : 1.2f;
            return PlayerPos(Seeker) + Vector3.up * h + MonsterForward() * 1.2f;
        }

        /// <summary>Per frame: host keeps the held item in front of the monster and checks hits of a thrown one.</summary>
        private void ItemTick()
        {
            if (!IsHost) return;
            if (_heldItem != null)
            {
                if (Seeker == null || Phase != Phase.Hunting) { _heldItem.useGravity = true; _heldItem = null; return; }
                Vector3 to = HoldPoint() - _heldItem.worldCenterOfMass;
                if (to.magnitude > 6f) { _heldItem.position += to; to = Vector3.zero; } // got stuck – snap back
                _heldItem.velocity = to * 12f;
                _heldItem.angularVelocity *= 0.8f;
                Compat.Call(_heldItem.GetComponent<PhysGrabObject>(), "OverrideIndestructible", 0.2f);
            }

            if (Phase != Phase.Hunting) { _flying.Clear(); return; }
            for (int i = _flying.Count - 1; i >= 0; i--)
            {
                var (rb, until, hit) = _flying[i];
                if (rb == null || Time.time >= until) { _flying.RemoveAt(i); continue; }
                float speed = rb.velocity.magnitude;
                if (speed < 5f) continue;
                Vector3 c = rb.worldCenterOfMass;
                int dmg = Mathf.Clamp(Mathf.RoundToInt(15f + speed * 1.2f + rb.mass * 6f), 20, 90);
                foreach (var h in AliveHiders())
                    if (Vector3.Distance(PlayerPos(h) + Vector3.up * 0.9f, c) < 1.2f && hit.Add(h.photonView.ViewID))
                        HostDamage(h, dmg, "thrown item");
                foreach (var d in AliveDummies())
                    if (Vector3.Distance(d.transform.position + Vector3.up * 0.9f, c) < 1.2f && hit.Add(-1000 - d.GetInstanceID()))
                        HostDamageDummy(d, dmg, c, "thrown item");
            }
        }

        // The held item must not collide with the monster: otherwise a big item pushes the Rugrat around when it looks
        // around. Collisions are switched off on every client (each moves its own player body) and back on shortly
        // after the throw.
        private readonly List<(Collider a, Collider b)> _itemIgnored = new List<(Collider, Collider)>();

        private void SetItemCollision(int viewId)
        {
            RestoreItemCollision();
            if (viewId == 0 || Seeker == null) return;
            var pv = Photon.Pun.PhotonView.Find(viewId);
            if (pv == null) return;
            var itemCols = pv.GetComponentsInChildren<Collider>(true);
            var mine = new List<Collider>(Seeker.GetComponentsInChildren<Collider>(true));
            if (GameRefs.IsLocal(Seeker) && PlayerController.instance != null)
                mine.AddRange(PlayerController.instance.GetComponentsInChildren<Collider>(true));
            foreach (var a in itemCols)
                foreach (var b in mine)
                {
                    if (a == null || b == null || a == b) continue;
                    Physics.IgnoreCollision(a, b, true);
                    _itemIgnored.Add((a, b));
                }
            Plugin.Log.LogInfo($"[Rugrat] held item ignores {mine.Count} monster colliders");
        }

        private void RestoreItemCollision()
        {
            foreach (var (a, b) in _itemIgnored)
                if (a != null && b != null) Physics.IgnoreCollision(a, b, false);
            _itemIgnored.Clear();
        }

        private void OnItemState(int state, int viewId = 0)
        {
            _holdingItem = state == 1;
            if (state == 1) SetItemCollision(viewId);
            else if (state == 2) Later(0.6f, () => { if (!_holdingItem) RestoreItemCollision(); }); // let it clear the monster first
            if (Seeker == null || !GameRefs.IsLocal(Seeker)) return;
            var col = Abilities.UnityColor(AbilityType.ItemThrow);
            if (state == 0) Banner(L.ItemNone, col, 1.5f);
            else if (state == 1) { Banner(L.ItemPicked, col, 1.5f); _specialReadyAt = Time.time + 0.3f; } // throw right away
        }

        private void ClearItem()
        {
            if (_heldItem != null) _heldItem.useGravity = true;
            _heldItem = null;
            _flying.Clear();
            _holdingItem = false;
            RestoreItemCollision();
        }
    }
}
