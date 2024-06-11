using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CoGSaveManager.Json
{
	public enum JsonNodeType { Custom, Stats, Temps };

	public class JsonNode
	{
		public readonly string Key;
		private string value;
		private readonly string jsonWrapperChars;
		public JsonNodeType NodeType;

		public bool IsBoolean { get { return /*string.IsNullOrEmpty( jsonWrapperChars ) &&*/ ( value == "true" || value == "false" ); } }
		public bool BooleanValue
		{
			get { return value == "true"; }
			set { this.value = ( value ? "true" : "false" ); }
		}

		// For string values ("KEY":"VALUE"), escape UTF8 characters
		public string StringValue
		{
			get { return ( jsonWrapperChars == "\"\"" ) ? Regex.Unescape( value ) : value; }
			set { this.value = ( jsonWrapperChars == "\"\"" ) ? EscapeString( value ) : value; }
		}

		public int JsonLength { get { return Key.Length + value.Length + 5; } } // "KEY":?VALUE? -> Here, 5 = "":?? where ?? are jsonWrapperChars (if not null)

		public JsonNode( string key, string value, string jsonWrapperChars, JsonNodeType nodeType )
		{
			this.Key = key;
			this.value = value;
			this.jsonWrapperChars = jsonWrapperChars;
			this.NodeType = nodeType;
		}

		public void AppendTo( StringBuilder stringBuilder )
		{
			stringBuilder.Append( "\"" ).Append( Key ).Append( "\":" );

			if( !string.IsNullOrEmpty( jsonWrapperChars ) )
				stringBuilder.Append( jsonWrapperChars[0] );

			stringBuilder.Append( value );

			if( !string.IsNullOrEmpty( jsonWrapperChars ) )
				stringBuilder.Append( jsonWrapperChars[1] );
		}

		// Credit: https://github.com/mono/mono/blob/22da2fe31ef898e7021fad59521ee0b329d37b07/mcs/class/System.Web/System.Web/HttpUtility.cs#L528
		// Alternative solution (not tested): https://stackoverflow.com/a/14087738/2373034
		private string EscapeString( string value )
		{
			StringBuilder sb = new StringBuilder( value.Length * 2 );
			for( int i = 0; i < value.Length; i++ )
			{
				char ch = value[i];
				if( ch >= 0 && ch <= 7 || ch == 11 || ch >= 14 && ch <= 31 || ch > 127 )
					sb.AppendFormat( "\\u{0:x4}", (int) ch );
				else
				{
					switch( (int) ch )
					{
						case 8: sb.Append( "\\b" ); break;
						case 9: sb.Append( "\\t" ); break;
						case 10: sb.Append( "\\n" ); break;
						case 12: sb.Append( "\\f" ); break;
						case 13: sb.Append( "\\r" ); break;
						case 34: sb.Append( "\\\"" ); break;
						case 92: sb.Append( "\\\\" ); break;
						default: sb.Append( ch ); break;
					}
				}
			}

			return sb.ToString();
		}
	}

	public class JsonSaveData
	{
		private readonly JsonNode[] allNodes;
		public readonly JsonNode[] VisibleNodes;

		public int JsonLength
		{
			get
			{
				int result = 50 + allNodes.Length; // '{' (+1), '}' (+1), '"stats":{...},' (+11), '"temps":{...},' (+11) and commas (+AllNodes.Length). To be safe, we are giving additional capacity instead of the required +24 chars
				for( int i = 0; i < allNodes.Length; i++ )
					result += allNodes[i].JsonLength;

				return result;
			}
		}

		public JsonSaveData( string json, int jsonStartIndex, int jsonEndIndex )
		{
			allNodes = FetchNodesFromJson( json, jsonStartIndex, jsonEndIndex, true ).ToArray();

			int visibleNodeCount = 0;
			for( int i = 0; i < allNodes.Length; i++ )
			{
				if( allNodes[i].NodeType != JsonNodeType.Custom )
					visibleNodeCount++;
			}

			VisibleNodes = new JsonNode[visibleNodeCount];
			for( int i = 0, index = 0; i < allNodes.Length; i++ )
			{
				if( allNodes[i].NodeType != JsonNodeType.Custom )
					VisibleNodes[index++] = allNodes[i];
			}
		}

		private List<JsonNode> FetchNodesFromJson( string json, int jsonStartIndex, int jsonEndIndex, bool isRootJson )
		{
			List<JsonNode> result = new List<JsonNode>();

			int index = jsonStartIndex;
			while( index < jsonEndIndex )
			{
				int keyStartIndex = json.IndexOf( '"', index, jsonEndIndex - index );
				if( keyStartIndex < 0 )
					break;

				int keyEndIndex = json.IndexOf( '"', keyStartIndex + 1 );
				string key = json.Substring( keyStartIndex + 1, keyEndIndex - keyStartIndex - 1 );

				string valueWrapperChars;
				int valueStartIndex = keyEndIndex + 2;
				int valueEndIndex = FindEndIndexOfValue( json, valueStartIndex, out valueWrapperChars );

				if( isRootJson && ( key == "stats" || key == "temps" ) )
				{
					JsonNodeType subNodeType = ( key == "stats" ) ? JsonNodeType.Stats : JsonNodeType.Temps;
					List<JsonNode> subNodes = FetchNodesFromJson( json, valueStartIndex, valueEndIndex, false );
					for( int i = 0; i < subNodes.Count; i++ )
						subNodes[i].NodeType = subNodeType;

					result.AddRange( subNodes );
				}
				else
				{
					string value;
					if( string.IsNullOrEmpty( valueWrapperChars ) ) // Value isn't wrapped by '{}', '[]' or '""'
						value = json.Substring( valueStartIndex, valueEndIndex - valueStartIndex + 1 );
					else // Value is wrapped by '{}', '[]' or '""'
						value = json.Substring( valueStartIndex + 1, valueEndIndex - valueStartIndex - 1 );

					result.Add( new JsonNode( key, value, valueWrapperChars, JsonNodeType.Custom ) );
				}

				index = valueEndIndex + 1;
			}

			return result;
		}

		private int FindEndIndexOfValue( string json, int valueStartIndex, out string valueWrapperChars )
		{
			char startChar = json[valueStartIndex];
			if( startChar == '{' || startChar == '[' )
			{
				List<char> pendingValueWrapperCharsStack = new List<char>( 4 );
				if( startChar == '{' )
					pendingValueWrapperCharsStack.Add( '}' );
				else
					pendingValueWrapperCharsStack.Add( ']' );

				int result = valueStartIndex;
				while( pendingValueWrapperCharsStack.Count > 0 )
				{
					result++;

					if( json[result] == pendingValueWrapperCharsStack[pendingValueWrapperCharsStack.Count - 1] )
						pendingValueWrapperCharsStack.RemoveAt( pendingValueWrapperCharsStack.Count - 1 );
					else
					{
						switch( json[result] )
						{
							case '[': pendingValueWrapperCharsStack.Add( ']' ); break;
							case '{': pendingValueWrapperCharsStack.Add( '}' ); break;
							case '"': result = FindEndIndexOfValue( json, result, out valueWrapperChars ); break;
						}
					}
				}

				valueWrapperChars = ( startChar == '{' ) ? "{}" : "[]";
				return result;
			}
			else if( startChar == '"' )
			{
				bool isLiteralQuote;
				int result;
				do
				{
					result = json.IndexOf( '"', valueStartIndex + 1 );
					valueStartIndex = result;

					isLiteralQuote = false;
					for( int i = result - 1; json[i] == '\\'; i-- )
						isLiteralQuote = !isLiteralQuote;
				} while( isLiteralQuote );

				valueWrapperChars = "\"\"";
				return result;
			}
			else
			{
				int result = valueStartIndex + 1;
				while( json[result] != ',' && json[result] != ']' && json[result] != '}' )
					result++;

				valueWrapperChars = null;
				return result - 1;
			}
		}

		public void ToJson( StringBuilder sb )
		{
			sb.Append( "{" );

			JsonNodeType prevNodeType = (JsonNodeType) 99999;
			for( int i = 0; i < allNodes.Length; i++ )
			{
				if( allNodes[i].NodeType != prevNodeType )
				{
					if( prevNodeType == JsonNodeType.Stats || prevNodeType == JsonNodeType.Temps )
						sb.Append( "}," );

					if( allNodes[i].NodeType == JsonNodeType.Stats )
						sb.Append( "\"stats\":{" );
					else if( allNodes[i].NodeType == JsonNodeType.Temps )
						sb.Append( "\"temps\":{" );

					prevNodeType = allNodes[i].NodeType;
				}

				allNodes[i].AppendTo( sb );

				if( i < allNodes.Length - 1 && ( prevNodeType == JsonNodeType.Custom || allNodes[i + 1].NodeType == prevNodeType ) )
					sb.Append( ',' );
			}

			sb.Append( "}" );
		}
	}
}