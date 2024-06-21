namespace CoGSaveManager
{
	public static class ExtensionFunctions
	{
		public static bool ContainsAt( this string str, string value, int index )
		{
			if( index + value.Length > str.Length )
				return false;

			for( int i = 0; i < value.Length; i++ )
			{
				if( str[index + i] != value[i] )
					return false;
			}

			return true;
		}
	}
}