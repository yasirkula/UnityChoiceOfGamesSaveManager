using UnityEngine;

namespace CoGSaveManager
{
	[CreateAssetMenu( fileName = "Settings", menuName = "Settings Asset", order = 111 )]
	public class Settings : ScriptableObject
	{
		public string RemoteGameDataURL;
		public bool IgnoreArticlesWhileSortingGames;
		public float UIResolution;
	}
}