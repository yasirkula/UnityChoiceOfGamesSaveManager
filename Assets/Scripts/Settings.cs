using UnityEngine;

namespace CoGSaveManager
{
	[CreateAssetMenu( fileName = "Settings", menuName = "Settings Asset", order = 111 )]
	public class Settings : ScriptableObject
	{
		[Tooltip( "Games' readable names will be fetched from the database at this URL." )]
		public string RemoteGameDataURL;
		[Tooltip( "If enabled, articles like 'a' and 'the' will be ignored while sorting the games by name." )]
		public bool IgnoreArticlesWhileSortingGames;
		[Tooltip( "As UI resolution increases, UI elements shrink in size." )]
		public float UIResolution;
		[Tooltip( "If enabled, texts will look more sharper at the possible cost of performance." )]
		public bool UIPixelPerfect;
		[Tooltip( "If enabled, the \"See Choices\" window will open automatically every time a new automated save is created (i.e. when user progresses through the story)." )]
		public bool AutoShowChoiceExplorer;
		[Tooltip( "If enabled, save manager will output more logs that can be useful for debugging purposes." )]
		public bool VerboseLogging;
	}
}