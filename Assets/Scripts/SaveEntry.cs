using System;
using System.IO;

namespace CoGSaveManager
{
	public class SaveEntry
	{
		public readonly string directory;
		public readonly string saveName;
		public readonly DateTime saveDate;

		private bool m_isStarred;
		public bool IsStarred
		{
			get { return m_isStarred; }
			set
			{
				if( m_isStarred == value )
					return;

				m_isStarred = value;

				if( m_isStarred )
					File.Create( Path.Combine( directory, SaveManager.STARRED_SAVE_FILE ) ).Close();
				else
					File.Delete( Path.Combine( directory, SaveManager.STARRED_SAVE_FILE ) );
			}
		}

		public SaveEntry( string directory, DateTime saveDate, string saveName = null )
		{
			this.directory = directory;
			this.saveDate = saveDate;
			this.saveName = string.IsNullOrEmpty( saveName ) ? saveDate.ToString( "G" ) : saveName;
			this.m_isStarred = File.Exists( Path.Combine( directory, SaveManager.STARRED_SAVE_FILE ) );
		}
	}
}