using System;

namespace CoGSaveManager
{
	public class SaveEntry
	{
		public readonly string directory;
		public readonly string saveName;
		public readonly DateTime saveDate;

		public SaveEntry( string directory, DateTime saveDate, string saveName = null )
		{
			this.directory = directory;
			this.saveDate = saveDate;
			this.saveName = string.IsNullOrEmpty( saveName ) ? saveDate.ToString( "G" ) : saveName;
		}
	}
}