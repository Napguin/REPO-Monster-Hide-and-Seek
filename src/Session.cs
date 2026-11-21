using Photon.Pun;
using UnityEngine;

namespace HideAndSeekMonster
{
    /// <summary>
    /// A new lobby (restarted, joined another one, back to the main menu) starts fresh: scores, item owners and
    /// everything of the after-round are forgotten. Map changes inside the same lobby keep them.
    /// </summary>
    public partial class HideAndSeekManager
    {
        private string _sessionRoom;

        private void SessionTick()
        {
            string room = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : null;
            bool menu = false;
            try { menu = SemiFunc.IsMainMenu(); } catch { /* game changed */ }
            if (menu) room = null;
            if (room == _sessionRoom) return;
            bool hadSession = _sessionRoom != null;
            _sessionRoom = room;
            if (hadSession || room == null) ResetSession();
        }

        private void ResetSession()
        {
            _totals.Clear();
            _totalNames.Clear();
            _sbNames = new string[0];
            _sbRound = new int[0];
            _sbTotal = new int[0];
            _sbSeeker = new bool[0];
            _resultsUntil = 0f;
            _itemOwner.Clear();
            _hostItemKey.Clear();
            _ownedSeenMap.Clear();
            _lockedItems.Clear();
            _nextSeekerKey = null;
            _selectedViewId = -1;
            CancelAfterRound();
            Plugin.Log.LogInfo("[Session] new lobby – scores and item owners reset");
        }
    }
}
