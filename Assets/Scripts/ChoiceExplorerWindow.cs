using SimpleJSON;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace CoGSaveManager
{
	[DefaultExecutionOrder( 1 )] /// Greater than <see cref="ScrollRect"/>.
	public partial class ChoiceExplorerWindow : MonoBehaviour
	{
		private enum TokenType { None, Variable, Bool, Number, String };
		private enum EvaluateFlags
		{
			/// <summary>
			/// If no flag is set, the whole line will be evaluated. So, the line "<c>some_variable >= 1</c>"s evaluation will return the comparison's result as <see cref="TokenType.Bool"/>.
			/// </summary>
			None = 0,
			/// <summary>
			/// If set, the line "<c>some_variable >= 1</c>"s evaluation will stop at <c>some_variable</c> and it will be returned as <see cref="TokenType.Variable"/>.
			/// </summary>
			StopAtFirstToken = 1 << 1,
			/// <summary>
			/// If set, the first encountered word will be returned as <see cref="TokenType.String"/>, without evaluating any functions or operations (i.e. "<c>label-1</c>" will be returned as "label-1"
			/// instead of attempting to evaluate "label" as <see cref="TokenType.Variable"/> and then subtracting 1 from it).
			/// </summary>
			StopAtFirstWord = 1 << 2,
			/// <summary>
			/// If set, evaluation will continue until encountering a whitespace. So the line "<c>some_variable >= 1</c>"s evaluation will stop at <c>some_variable</c> and it will be returned as
			/// <see cref="TokenType.Variable"/> but the line "<c>some_variable>=1</c>" will be fully evaluated and returned as <see cref="TokenType.Bool"/>.
			/// </summary>
			StopAtWhitespace = 1 << 3,
		};

		private const string TEST_SCENARIO_FILE = "ChoiceExplorerTest.txt";

#pragma warning disable 0649
		private class SceneData
		{
			public string Name;
			public int Size;
			public long Offset;
			public string[] Lines;
			public JSONObject Labels;

			public int GetLabelLineNumber( string label )
			{
				JSONNode value;
				if( Labels.TryGetValue( label, out value ) )
					return value.AsInt;

				StringBuilder sb = new StringBuilder( 100 + Labels.Count * 50 );
				sb.Append( "Label '" ).Append( label ).Append( "' isn't found in scene '" ).Append( Name ).Append( "'. All labels:" );
				foreach( KeyValuePair<string, JSONNode> kvPair in Labels )
					sb.Append( "\n- " ).Append( kvPair.Value.RawValue ).Append( ": " ).Append( kvPair.Key );

				throw new Exception( sb.ToString() );
			}
		}

		private struct Token
		{
			public readonly TokenType Type;
			public readonly object Value;

			// These static constructors allow type safety while creating new tokens
			public static Token Null() { return new Token( TokenType.None, null ); }
			public static Token Variable( string value ) { return new Token( TokenType.Variable, value ); }
			public static Token Bool( bool value ) { return new Token( TokenType.Bool, value ); }
			public static Token Number( double value ) { return new Token( TokenType.Number, value ); }
			public static Token String( string value ) { return new Token( TokenType.String, value ); }

			private Token( TokenType type, object value )
			{
				Value = value;
				Type = type;
			}

			public bool AsBool()
			{
				return AsType<bool>( TokenType.Bool );
			}

			public double AsDouble()
			{
				return AsType<double>( TokenType.Number );
			}

			public int AsInt()
			{
				return Mathf.RoundToInt( (float) AsDouble() );
			}

			public string AsString()
			{
				return AsType<string>( TokenType.String );
			}

			public string AsVariableName()
			{
				return Value.ToString();
			}

			private T AsType<T>( TokenType nativeType )
			{
				if( Type == nativeType )
					return (T) Value;

				if( Type == TokenType.Variable )
					return GetVariable<T>( (string) Value );
				else
				{
					try
					{
						return (T) Convert.ChangeType( Value, typeof( T ), CultureInfo.InvariantCulture );
					}
					catch
					{
						LogError( "Couldn't convert {0} to {1}", this, nativeType );
						return default( T );
					}
				}
			}

			public Token UnpackVariable()
			{
				object value;
				if( Type == TokenType.Variable && TryGetVariable( (string) Value, out value ) )
				{
					if( value is bool )
						return Bool( (bool) value );
					else if( value is string )
						return String( (string) value );
					else if( value is long )
						return Number( (long) value );
					else if( value is double )
						return Number( (double) value );
					else
						Assertion( value == null, "Unrecognized token ({0}) value: {1} ({2})", this, value, value.GetType() );
				}

				return this;
			}

			public static implicit operator JSONNode( Token token )
			{
				object value = token.Value;
				if( token.Type != TokenType.None && ( token.Type != TokenType.Variable || TryGetVariable( (string) value, out value ) ) )
				{
					if( value is bool )
						return new JSONBool( (bool) value );
					else if( value is string )
						return new JSONString( (string) value );
					else if( value is long )
						return new JSONNumber( (long) value );
					else if( value is double )
						return new JSONNumber( (double) value );
					else
						Assertion( value == null, "Unrecognized token ({0}) value: {1} ({2})", token, value, value.GetType() );
				}

				return JSONNull.CreateOrGet();
			}

			public bool ValueEquals( Token other )
			{
				try
				{
					/// Ignore <see cref="TokenType.None"/>.
					if( Type == TokenType.None || other.Type == TokenType.None )
						return false;

					/// Unpack <see cref="TokenType.Variable"/> if possible.
					Token self = UnpackVariable();
					other = other.UnpackVariable();
					if( self.Type == TokenType.Variable || other.Type == TokenType.Variable )
						return self.Type == other.Type && (string) self.Value == (string) other.Value;

					/// Convert <see cref="TokenType.Bool"/> to <see cref="TokenType.Number"/> to match ChoiceScript's behaviour.
					if( self.Type == TokenType.Bool )
						self = Number( (bool) self.Value ? 1.0 : 0.0 );
					if( other.Type == TokenType.Bool )
						other = Number( (bool) other.Value ? 1.0 : 0.0 );

					Assertion( ( self.Type == TokenType.Number || self.Type == TokenType.String ) && ( other.Type == TokenType.Number || other.Type == TokenType.String ), "Unexpected token type: {0}, {1}", self.Type, other.Type );

					/// Compare two <see cref="TokenType.String"/> values if possible.
					if( self.Type == TokenType.String && other.Type == TokenType.String )
						return (string) self.Value == (string) other.Value;

					/// Try comparing both values numerically because ("10" = 10) is true in ChoiceScript (not using string comparison because we need to use <see cref="Mathf.Approximately"/>).
					return Mathf.Approximately( Convert.ToSingle( self.Value, CultureInfo.InvariantCulture ), Convert.ToSingle( other.Value, CultureInfo.InvariantCulture ) );
				}
				catch { }

				return false;
			}

			public override string ToString()
			{
				object variableValue;
				if( Type == TokenType.Variable && TryGetVariable( (string) Value, out variableValue ) )
					return string.Format( "'{0}:{1}' ({2}<{3}>)", Value, variableValue, Type, ( variableValue != null ) ? variableValue.GetType().Name : "Null" );
				else
					return string.Format( "'{0}' ({1})", Value, Type );
			}
		}

		/// <returns>
		/// Unity complains about 65k vertex limit for long texts, so the text is split into multiple parts.
		/// </returns>
		private const int MaxStringLength = 10000;
		private const int LoadMoreLinesCount = 50;

		private const string ChoiceLineColor = "red";
		private const string ConditionLineColor = "blue";
		private const string VariableSetterLineColor = "magenta";
		private const string OtherCommandLineColor = "#11A200";
		private const string VariableValueColor = "#FF5E00";

		private readonly HashSet<string> highlightVariablesInCommands = new HashSet<string>() { "if", "elseif", "elsif", "set", "setref", "config", "temp", "create", "temp_array", "create_array" };

		[SerializeField]
		private Button copyTextButton, wrapLinesButton, toggleFontSizeButton, loadMoreLinesAtTopButton, loadMoreLinesAtBottomButton, resetMoreLinesButton, closeButton;

		[SerializeField]
		private Color wrapLinesActiveColor;
		private Color wrapLinesInactiveColor;

		[SerializeField]
		private Canvas canvas;
		private float CanvasScale { get { return canvas.transform.localScale.x; } }

		[SerializeField]
		private ScrollRect scrollView;

		[SerializeField]
		private VerticalLayoutGroup entriesLayoutGroup;

		[SerializeField]
		private ContentSizeFitter entriesContentSizeFitter;

		[SerializeField]
		private PointerEventListener entriesPointerEventListener;

		[SerializeField]
		private Text entryPrefab;
		private readonly List<Text> activeEntries = new List<Text>( 4 );
		private readonly Stack<Text> pooledEntries = new Stack<Text>( 4 );
#pragma warning restore 0649

		private Dictionary<string, SceneData> scenesLookup;
		private List<string> scenesList;
		private SceneData scene;
		private int topLineNumber, bottomLineNumber;
		private static string currentLine, lastErrorLine;
		private static int parsedStringDepth;

		private static JSONNode saveData;
		private string savePath;
		private string gameAsarFilePath;
		private static bool mergedPreviousBooksStats;

		private float contentPrevWidth;
		private static readonly StringBuilder sb = new StringBuilder( 12000 );

		private bool WrapLines
		{
			get { return PlayerPrefs.GetInt( "WrapLines", 0 ) == 1; }
			set
			{
				PlayerPrefs.SetInt( "WrapLines", value ? 1 : 0 );
				PlayerPrefs.Save();
			}
		}

		private int FontSize
		{
			get { return PlayerPrefs.GetInt( "ChoiceExplorerFontSize", entryPrefab.fontSize ); }
			set
			{
				PlayerPrefs.SetInt( "ChoiceExplorerFontSize", value );
				PlayerPrefs.Save();
			}
		}

		private void Awake()
		{
			wrapLinesInactiveColor = wrapLinesButton.targetGraphic.color;

			SetWrapLines( WrapLines );

			scrollView.onValueChanged.AddListener( ( scrollPos ) => RefreshLoadMoreLinesButtonPositions() );
			entriesPointerEventListener.OnClick = OnEntryClicked;
			copyTextButton.onClick.AddListener( CopyAllEntries );
			wrapLinesButton.onClick.AddListener( () => SetWrapLines( !WrapLines ) );
			toggleFontSizeButton.onClick.AddListener( () => SetFontSize( Mathf.Clamp( ( FontSize + 1 ) % 21, 10, 20 ) ) );
			loadMoreLinesAtTopButton.onClick.AddListener( () => LoadMoreLines( false ) );
			loadMoreLinesAtBottomButton.onClick.AddListener( () => LoadMoreLines( true ) );
			resetMoreLinesButton.onClick.AddListener( OnNewSaveOpened );
			closeButton.onClick.AddListener( () => gameObject.SetActive( false ) );
		}

		private void Update()
		{
			if( contentPrevWidth != scrollView.content.rect.width )
				RefreshEntriesLayoutGroup();
		}

		public void Show( string savePath, string gameAsarFilePath )
		{
			gameObject.SetActive( true );

			if( this.savePath != savePath )
			{
				this.savePath = savePath;

				if( this.gameAsarFilePath != gameAsarFilePath )
				{
					this.gameAsarFilePath = gameAsarFilePath;
					scenesLookup = null;
				}

				OnNewSaveOpened();
			}
		}

		private void OnNewSaveOpened()
		{
			scene = null;
			topLineNumber = int.MinValue;
			bottomLineNumber = int.MaxValue;
			currentLine = lastErrorLine = null;
			mergedPreviousBooksStats = false;
			indentationHighlightStartEntry = indentationHighlightEndEntry = null;

			string[] extractedTexts;
			string saveFileContents = File.ReadAllText( savePath );
			if( saveFileContents.IndexOf( '{' ) >= 0 )
				saveData = JSON.Parse( saveFileContents.Substring( saveFileContents.IndexOf( '{' ) ) );
			else
			{
				// Save file is blank at the very beginning of the game. Start from the beginning of the 'startup' scene in that case.
				saveData = new JSONObject();
				saveData["stats"]["sceneName"] = "startup";
				saveData["lineNum"] = 0;
			}

			extractedTexts = ExtractChoices();
			if( extractedTexts.Length == 0 )
				extractedTexts = new string[] { "No choice found..." };

			foreach( Text oldEntry in activeEntries )
			{
				oldEntry.gameObject.SetActive( false );
				oldEntry.rectTransform.SetAsLastSibling();

				pooledEntries.Push( oldEntry );
			}

			activeEntries.Clear();

			for( int i = 0; i < extractedTexts.Length; i++ )
				InsertNewEntryAt( i, extractedTexts[i] );

			RefreshLoadMoreLinesButtonVisibilities();
			resetMoreLinesButton.gameObject.SetActive( false );

			RefreshEntriesLayoutGroup();

			// Scroll to the top
			scrollView.verticalNormalizedPosition = 1f;
		}

		private void LoadMoreLines( bool downwards, int lineCount = LoadMoreLinesCount )
		{
			if( downwards )
			{
				lineCount = Mathf.Min( bottomLineNumber + lineCount, scene.Lines.Length - 1 ) - bottomLineNumber;
				string[] texts = ExtractLines( bottomLineNumber + 1, lineCount );
				bottomLineNumber += lineCount;

				for( int i = 0; i < texts.Length; i++ )
					InsertNewEntryAt( activeEntries.Count, texts[i] );
			}
			else
			{
				lineCount = topLineNumber - Mathf.Max( topLineNumber - lineCount, 0 );
				topLineNumber -= lineCount;
				string[] texts = ExtractLines( topLineNumber, lineCount );

				for( int i = 0; i < texts.Length; i++ )
					InsertNewEntryAt( i, texts[i] );
			}

			float prevContentHeight = scrollView.content.sizeDelta.y;

			RefreshLoadMoreLinesButtonVisibilities();
			resetMoreLinesButton.gameObject.SetActive( true );

			RefreshEntriesLayoutGroup();

			// Try to remain at the same text position after loading more lines upwards
			if( !downwards )
				scrollView.content.anchoredPosition = scrollView.ClampScrollPosition( scrollView.content.anchoredPosition + new Vector2( 0f, scrollView.content.sizeDelta.y - prevContentHeight ) );
		}

		private void InsertNewEntryAt( int index, string text )
		{
			Text entry;
			if( pooledEntries.Count > 0 )
			{
				entry = pooledEntries.Pop();
				entry.gameObject.SetActive( true );
			}
			else
				entry = Instantiate( entryPrefab, scrollView.content, false );

			entry.fontSize = FontSize;
			entry.horizontalOverflow = WrapLines ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;
			entry.text = text;
			entry.rectTransform.SetSiblingIndex( index + 1 ); /// The first sibling index belongs to <see cref="loadMoreLinesAtTopButton"/>.
			activeEntries.Insert( index, entry );
		}

		private void CopyAllEntries()
		{
			sb.Length = 0;

			// If Shift key is held, copy the whole scene contents. Otherwise, copy only the visible lines.
			if( !Input.GetKey( KeyCode.LeftShift ) && !Input.GetKey( KeyCode.RightShift ) )
			{
				foreach( Text entry in activeEntries )
					sb.Append( entry.text ).Append( '\n' );

				sb.Replace( "<b>", "" ).Replace( "</b>", "" ).Replace( "<color=" + ChoiceLineColor + ">", "" ).Replace( "<color=" + ConditionLineColor + ">", "" )
					.Replace( "<color=" + VariableSetterLineColor + ">", "" ).Replace( "<color=" + OtherCommandLineColor + ">", "" ).Replace( "<color=" + VariableValueColor + ">", "" ).Replace( "</color>", "" );
			}
			else if( scene != null )
			{
				foreach( string line in scene.Lines )
					sb.Append( line ).Append( '\n' );
			}

			GUIUtility.systemCopyBuffer = sb.ToString();
		}

		private void SetWrapLines( bool wrapLines )
		{
			using( new PreserveScrollPositionWithinScope( this ) )
			{
				WrapLines = wrapLines;

				wrapLinesButton.targetGraphic.color = wrapLines ? wrapLinesActiveColor : wrapLinesInactiveColor;
				entriesContentSizeFitter.horizontalFit = wrapLines ? ContentSizeFitter.FitMode.Unconstrained : ContentSizeFitter.FitMode.PreferredSize;

				if( wrapLines )
				{
					scrollView.content.anchoredPosition = new Vector2( 0f, scrollView.content.anchoredPosition.y );
					scrollView.content.sizeDelta = new Vector2( 0f, scrollView.content.sizeDelta.y );
				}

				foreach( Text entry in activeEntries )
					entry.horizontalOverflow = wrapLines ? HorizontalWrapMode.Wrap : HorizontalWrapMode.Overflow;

				RefreshEntriesLayoutGroup();
			}
		}

		private void SetFontSize( int fontSize )
		{
			using( new PreserveScrollPositionWithinScope( this ) )
			{
				FontSize = fontSize;

				foreach( Text entry in activeEntries )
					entry.fontSize = fontSize;

				RefreshEntriesLayoutGroup();
			}
		}

		private void RefreshEntriesLayoutGroup()
		{
			entriesContentSizeFitter.enabled = true;
			entriesLayoutGroup.enabled = true;

			LayoutRebuilder.ForceRebuildLayoutImmediate( (RectTransform) scrollView.transform );
			Canvas.ForceUpdateCanvases();

			// Make sure that the content doesn't go out-of-bounds
			scrollView.verticalNormalizedPosition = Mathf.Max( 0.00001f, scrollView.verticalNormalizedPosition );
			scrollView.OnScroll( new PointerEventData( EventSystem.current ) );

			entriesContentSizeFitter.enabled = false;
			entriesLayoutGroup.enabled = false;
			contentPrevWidth = scrollView.content.rect.width;

			RefreshLoadMoreLinesButtonPositions();
			RefreshIndentationHighlight();

			if( lineHighlightCoroutine != null )
			{
				StopCoroutine( lineHighlightCoroutine );
				lineHighlightCoroutine = null;
				lineHighlight.SetOpacity( 0f );
			}
		}

		private void RefreshLoadMoreLinesButtonVisibilities()
		{
			/// <see cref="scene"/> can be null if the save file is empty (i.e. the user hasn't progressed through the game yet).
			loadMoreLinesAtTopButton.gameObject.SetActive( scene != null && topLineNumber > 0 );
			loadMoreLinesAtBottomButton.gameObject.SetActive( scene != null && bottomLineNumber < scene.Lines.Length - 1 );
		}

		private void RefreshLoadMoreLinesButtonPositions()
		{
			Vector2 position = new Vector2( -scrollView.content.anchoredPosition.x, 0f );
			Vector2 size = new Vector2( scrollView.viewport.rect.width - entriesLayoutGroup.padding.horizontal, loadMoreLinesAtTopButton.targetGraphic.rectTransform.sizeDelta.y );
			loadMoreLinesAtTopButton.targetGraphic.rectTransform.anchoredPosition = position;
			loadMoreLinesAtBottomButton.targetGraphic.rectTransform.anchoredPosition = position;
			loadMoreLinesAtTopButton.targetGraphic.rectTransform.sizeDelta = size;
			loadMoreLinesAtBottomButton.targetGraphic.rectTransform.sizeDelta = size;
		}

		private void ExtractScenesList( FileStream fs )
		{
			byte[] bytes = new byte[50000];
			fs.Read( bytes, 0, bytes.Length );
			string str = Encoding.UTF8.GetString( bytes );
			int startIndex = str.IndexOf( "{\"" ), endIndex = startIndex + 1;
			for( int curlyBraces = 1; curlyBraces > 0; endIndex++ )
			{
				switch( str[endIndex] )
				{
					case '{': curlyBraces++; break;
					case '}': curlyBraces--; break;
				}
			}

			long seekOffset = str.IndexOf( "const", endIndex );
			scenesLookup = new Dictionary<string, SceneData>( 64 );
			scenesList = new List<string>( 64 );

			JSONNode scenesNode = JSON.Parse( str.Substring( startIndex, endIndex - startIndex ) )["files"]["deploy"]["files"]["scenes"]["files"];
			foreach( KeyValuePair<string, JSONNode> kvPair in scenesNode )
			{
				if( kvPair.Key.EndsWith( ".txt.json", StringComparison.OrdinalIgnoreCase ) )
				{
					string name = kvPair.Key.Substring( 0, kvPair.Key.Length - ".txt.json".Length );
					scenesLookup[name] = new SceneData()
					{
						Name = name,
						Size = kvPair.Value["size"],
						Offset = kvPair.Value["offset"] + seekOffset,
					};
				}
			}

			scene = GetScene( fs, "startup" );
			int scenesListLineNumber = Array.IndexOf( scene.Lines, "*scene_list" );
			Assertion( scenesListLineNumber >= 0, "*scene_list command couldn't be found in startup.txt!" );
			for( int i = scenesListLineNumber + 1; i < scene.Lines.Length && CalculateLineIndentation( i ) > 0; i++ )
			{
				if( IsLineWhitespace( i ) )
					continue;

				string name = scene.Lines[i].Replace( "$", "" ).Trim();
				if( name.Length > 0 )
					scenesList.Add( name );
			}

			scene = null; /// So that it can be fetched from <see cref="saveData"/> at the beginning of <see cref="ExtractChoices"/>.
		}

		private SceneData GetScene( FileStream fs, string sceneName )
		{
			SceneData scene;
			if( !scenesLookup.TryGetValue( sceneName, out scene ) )
			{
				StringBuilder sb = new StringBuilder( 100 + scenesLookup.Count * 50 );
				sb.Append( "Scene '" ).Append( sceneName ).Append( "' isn't found. All scenes:" );
				foreach( string _sceneName in scenesLookup.Keys )
					sb.Append( "\n- " ).Append( _sceneName );

				throw new Exception( sb.ToString() );
			}

			if( scene.Lines == null )
			{
				fs.Seek( scene.Offset, SeekOrigin.Begin );
				byte[] bytes = new byte[scene.Size];
				fs.Read( bytes, 0, bytes.Length );

				JSONNode sceneNode = JSON.Parse( Encoding.UTF8.GetString( bytes ) );
				scene.Labels = sceneNode["labels"].AsObject;

				JSONArray linesNode = sceneNode["lines"].AsArray;
				scene.Lines = new string[linesNode.Count + 1];
				for( int i = 0; i < linesNode.Count; i++ )
					scene.Lines[i] = linesNode[i];

				// If the scene ends without a proper command like "*finish" or "*goto_scene", ChoiceScript automatically adds a "*finish" command at the end (Scene.prototype.autofinish)
				scene.Lines[scene.Lines.Length - 1] = "*finish";

				// Convert all space-indentations to tab-indentations because for games that use 2-space-indentations, it becomes harder to follow where an "*if" command or choice option starts and ends
				bool isUsingSpaceIndentation = true;
				foreach( string line in scene.Lines )
				{
					if( line.StartsWith( "\t" ) )
					{
						isUsingSpaceIndentation = false;
						break;
					}
				}

				if( isUsingSpaceIndentation )
				{
					// ChoiceScript doesn't enforce a standardized space-indentation across whole game (some indentations might be 1-space while others might be 2-space). So we have to follow a dynamic approach:
					// - If space-indentation increases, tab-indentation increases by 1
					// - If space-indentation decreases, cached tab-indentations up to that space-indentation will be removed
					// The following example shows the contents of 'tabIndentations' list at each line. The 1st element is always 0 because 0-space indentation corresponds to 0-tab indentation:
					//*if true (0)        (0-space-indentation, 0-tab-indentation)
					//  Hello  (0, 1, 1)  (2-space-indentation, 1-tab-indentation)
					// World   (0, 1)     (1-space-indentation, 1-tab-indentation)
					//*else    (0)        (0-space-indentation, 0-tab-indentation)
					// Help    (0, 1)     (1-space-indentation, 1-tab-indentation)
					List<int> tabIndentations = new List<int>( 32 ) { 0 };
					for( int i = 0; i < scene.Lines.Length; i++ )
					{
						string line = scene.Lines[i];
						int indentation = CalculateLineIndentation( line );
						if( indentation >= line.Length ) // Skip whitespace lines
							continue;

						// If space-indentation is 2, the list should end at [2], i.e. its size should be 3
						int delta = indentation + 1 - tabIndentations.Count;
						if( delta > 0 )
						{
							// Increment tab-indentation by 1
							int tab = tabIndentations[tabIndentations.Count - 1] + 1;
							for( int j = 0; j < delta; j++ )
								tabIndentations.Add( tab );
						}
						else if( delta < 0 )
						{
							// Reset excessive tab-indentations
							tabIndentations.RemoveRange( tabIndentations.Count + delta, -delta );
						}

						if( indentation > 0 )
							scene.Lines[i] = line.Substring( indentation ).Insert( 0, new string( '\t', tabIndentations[indentation] ) );
					}
				}
			}

			return scene;
		}

		private string[] ExtractChoices()
		{
			List<string> result = new List<string>( 2 );
			try
			{
				using( FileStream fs = new FileStream( gameAsarFilePath, FileMode.Open, FileAccess.Read ) )
					ExtractChoicesInternal( fs, result );
			}
			catch( Exception e )
			{
				LogError( "Critical error while extracting choices: {0}", e );
			}

			return PostProcessExtractedResults( result );
		}

		private void ExtractChoicesInternal( FileStream fs, List<string> result )
		{
			sb.Length = 0;

			if( scenesLookup == null )
				ExtractScenesList( fs );
			if( scene == null )
				scene = GetScene( fs, saveData["stats"]["sceneName"] );

			/// <see cref="TEST_SCENARIO_FILE"/> allows testing the parser with a custom scenario.
			if( File.Exists( TEST_SCENARIO_FILE ) )
			{
				string[] lines = File.ReadAllText( TEST_SCENARIO_FILE ).Replace( "\r", "" ).Split( '\n' );
				if( lines[0] != "*skip_test" )
				{
					scene.Lines = lines;
					scene.Labels.Clear();
					saveData["lineNum"] = 0;
					saveData["stats"].Clear();
					saveData["temps"].Clear();

					for( int i = 0; i < scene.Lines.Length; i++ )
					{
						string line = scene.Lines[i].Trim();
						if( line.StartsWith( "*label" ) )
							scene.Labels[line.Substring( 7 )] = i;
					}
				}
			}

			// Whether or not any plaintext, image or similar page content is encountered while parsing the page (opposite of "screenEmpty" property in ChoiceScript)
			bool anyPageContentEncountered = false;

			int lineNumber = saveData["lineNum"];
			SaveManager.LogVerbose( "Extracting choices from '{0}', starting with line {1}: {2}", savePath, lineNumber, scene.Lines[lineNumber].Trim() );

			for( ; lineNumber < scene.Lines.Length; lineNumber++ )
			{
				string line = OnCurrentLineChanged( scene.Lines[lineNumber].Trim() );
				if( IsLineChoiceOption( line ) )
				{
					// If we encounter a choice without first entering a "*choice" or "*fake_choice" command, then we should skip its contents
					SaveManager.LogVerbose( "Skipped choice: {0}", line );

					int indentation = CalculateLineIndentation( lineNumber );
					while( lineNumber < scene.Lines.Length - 1 && ( IsLineWhitespace( lineNumber + 1 ) || CalculateLineIndentation( lineNumber + 1 ) >= indentation ) )
						lineNumber++;

					continue;
				}

				string command = GetCommandName( line );
				if( string.IsNullOrEmpty( command ) )
				{
					if( !IsLineWhitespace( lineNumber ) ) // It's plaintext/paragraph
						anyPageContentEncountered = true;

					continue;
				}

				if( command == "choice" || command == "fake_choice" )
				{
					topLineNumber = lineNumber;
					AppendLineToResult( scene.Lines[lineNumber], result );
					int indentation = CalculateLineIndentation( lineNumber );
					for( lineNumber++; lineNumber < scene.Lines.Length && ( IsLineWhitespace( lineNumber ) || CalculateLineIndentation( lineNumber ) > indentation ); lineNumber++ )
						AppendLineToResult( scene.Lines[lineNumber], result );

					bottomLineNumber = lineNumber - 1;
					break;
				}
				else if( command == "input_text" || command == "input_number" )
				{
					topLineNumber = lineNumber;
					for( int i = 0; i < 50 && lineNumber < scene.Lines.Length; i++, lineNumber++ )
						AppendLineToResult( scene.Lines[lineNumber], result );

					bottomLineNumber = lineNumber - 1;
					break;
				}
				else if( command == "if" || command == "elseif" || command == "elsif" || command == "else" )
				{
					int indentation = CalculateLineIndentation( lineNumber );
					bool canEvaluateElseCondition = false;
					while( lineNumber < scene.Lines.Length )
					{
						line = OnCurrentLineChanged( scene.Lines[lineNumber].Trim() );
						command = GetCommandName( line );
						if( command == "if" )
						{
							if( EvaluateCondition( line, lineNumber, command.Length + 1 ) )
								break;
							else
								canEvaluateElseCondition = true;
						}
						else if( command == "elseif" || command == "elsif" )
						{
							if( canEvaluateElseCondition && EvaluateCondition( line, lineNumber, command.Length + 1 ) )
								break;
						}
						else if( command == "else" )
						{
							if( canEvaluateElseCondition )
								break;
						}
						else
						{
							lineNumber--; // It will be incremented back by the outer for-loop
							break;
						}

						if( !canEvaluateElseCondition )
							SaveManager.LogVerbose( "Skipped condition at line {0}: {1}", lineNumber, line );

						// Condition wasn't met, skip its contents
						lineNumber++;
						while( lineNumber < scene.Lines.Length && ( IsLineWhitespace( lineNumber ) || CalculateLineIndentation( lineNumber ) > indentation ) )
							lineNumber++;

						if( lineNumber < scene.Lines.Length && CalculateLineIndentation( lineNumber ) != indentation )
						{
							lineNumber--; // It will be incremented back by the outer for-loop
							break;
						}
					}
				}
				else if( command == "temp" || command == "create" )
				{
					SaveManager.LogVerbose( "<b>=== Evaluating expression {0}: {1}</b>", lineNumber, line );

					List<Token> arguments = GetCommandArguments( line, EvaluateFlags.StopAtFirstToken, EvaluateFlags.None );
					Token value = ( arguments.Count > 1 ) ? arguments[1] : Token.Null(); // "*temp"s second parameter is optional so it may not exist
					SetVariable( arguments[0].AsVariableName(), value, command == "temp" );

					SaveManager.LogVerbose( "<b>=== Evaluated expression {0}</b>", value );
				}
				else if( command == "temp_array" || command == "create_array" )
				{
					List<Token> arguments = GetCommandArguments( line );
					string arrayName = arguments[0].AsVariableName();
					int arrayLength = arguments[1].AsInt();
					SetVariable( arrayName + "_count", arrayLength, command == "temp_array" );
					for( int i = 0; i < arrayLength; i++ )
					{
						Token value = ( arguments.Count == 3 ) ? arguments[2] : arguments[i + 2]; // If only a single value is provided, all array elements share the same value
						SetVariable( arrayName + "_" + ( i + 1 ), value, command == "temp_array" );
					}
				}
				else if( command == "set" || command == "setref" || command == "config" )
				{
					SaveManager.LogVerbose( "<b>=== Evaluating expression {0}: {1}</b>", lineNumber, line );

					int index = command.Length + 1;
					Token variable = EvaluateExpression( line, ref index, 0, EvaluateFlags.StopAtFirstToken );
					string variableName = ( command == "setref" ) ? variable.AsString() : variable.AsVariableName();
					Token value = EvaluateExpression( line, ref index, implicitVariable: variableName );
					SetVariable( variableName, value );

					SaveManager.LogVerbose( "<b>=== Evaluated expression {0}</b>", value );
				}
				else if( command == "rand" )
				{
					SaveManager.LogVerbose( "<b>=== Evaluating expression {0}: {1}</b>", lineNumber, line );

					List<Token> arguments = GetCommandArguments( line );
					Assertion( arguments.Count == 3, "*rand has {0} argument(s) but it expects 3", arguments.Count );

					// If min and max values are provided as integers, then an integer in range [minValue, maxValue] will be returned.
					// Otherwise, a double in range [minValue, maxValue) will be returned.
					Token value;
					double minValue = arguments[1].AsDouble(), maxValue = arguments[2].AsDouble();
					if( minValue == maxValue )
						value = Token.Number( minValue );
					else if( minValue == (int) minValue && maxValue == (int) maxValue )
						value = Token.Number( Random.Range( (int) minValue, (int) maxValue + 1 ) );
					else
						value = Token.Number( Random.Range( (float) minValue, (float) maxValue - Mathf.Epsilon ) );

					SetVariable( arguments[0].AsVariableName(), value );

					SaveManager.LogVerbose( "<b>=== Evaluated expression {0}</b>", value );
				}
				else if( command == "params" )
				{
					List<Token> paramNames = GetCommandArguments( line );
					JSONArray paramValues = saveData["temps"]["param"].AsArray;
					Assertion( paramNames.Count <= paramValues.Count, "*params has {0} argument(s) but subroutine has {1} value(s)", paramNames.Count, paramValues.Count );

					SetVariable( "param_count", paramValues.Count, true );
					for( int i = 0; i < paramValues.Count; i++ )
					{
						SetVariable( "param_" + ( i + 1 ), paramValues[i].Clone(), true );
						if( i < paramNames.Count )
							SetVariable( paramNames[i].AsVariableName(), paramValues[i].Clone(), true );
					}
				}
				else if( command == "finish" || command == "finish_advertisement" )
				{
					// "*finish" will show a "Next Chapter" button if the page isn't empty
					if( anyPageContentEncountered )
						break;

					// 'startup' scene may not appear in "*scene_list" (it doesn't in 'The Fernweh Saga: Book One')
					int currentSceneIndex = scenesList.IndexOf( scene.Name );
					Assertion( currentSceneIndex >= 0 || scene.Name.Equals( "startup", StringComparison.OrdinalIgnoreCase ), "*scene_list doesn't contain {0}", scene.Name );
					if( currentSceneIndex < scenesList.Count - 1 )
					{
						scene = GetScene( fs, scenesList[currentSceneIndex + 1] );
						lineNumber = -1;
					}
					else
						break;
				}
				else if( command == "goto" || command == "gosub" || command == "gotoref" )
				{
					if( command == "gosub" )
					{
						JSONObject stackEntry = new JSONObject();
						stackEntry["lineNum"] = lineNumber;
						stackEntry["indent"] = CalculateLineIndentation( lineNumber );
						saveData["temps"]["choice_substack"].Add( stackEntry );
					}

					List<Token> arguments;
					ParseGosubCommand( command, line, out lineNumber, out arguments );
					if( command == "gosub" )
						SetVariable( "param", new JSONArray( ( arguments.Count > 1 ) ? arguments.GetRange( 1, arguments.Count - 1 ).ConvertAll( ( token ) => (JSONNode) token ) : new List<JSONNode>() ), true );
				}
				else if( command == "goto_scene" || command == "gosub_scene" )
				{
					if( command == "gosub_scene" )
					{
						JSONObject stackEntry = new JSONObject();
						stackEntry["name"] = scene.Name;
						stackEntry["lineNum"] = lineNumber + 1;
						stackEntry["indent"] = CalculateLineIndentation( lineNumber );
						stackEntry["temps"] = saveData["temps"];
						saveData["stats"]["choice_subscene_stack"].Add( stackEntry );
					}

					/// Not using <see cref="Token.AsString"/> while reading scene and label names (same goes for "goto" and "gosub" commands) because their
					/// string values are entered without quotation marks in these commands and thus, they're interpreted as <see cref="TokenType.Variable"/>s.
					/// Calling <see cref="Token.AsString"/> on them would attempt to fetch their non-existent variable values instead of just returning their
					/// <see cref="Token.Value"/>s. Calling <see cref="Token.AsVariableName"/> will simply return their <see cref="Token.Value"/>s.
					/// -----
					/// If the line contains '{' or '[' characters, then the scene and label names are evaluated as standard tokens by ChoiceScript. Otherwise,
					/// the first two words are assigned to them as is (i.e. "goto_scene scene-1 label+1" will set scene to "scene-1" and label to "label+1").
					EvaluateFlags sceneAndLabelNameEvaluationFlags = ( line.IndexOf( '{' ) >= 0 || line.IndexOf( '[' ) >= 0 ) ? EvaluateFlags.StopAtFirstToken : EvaluateFlags.StopAtFirstWord;
					List<Token> arguments = GetCommandArguments( line, sceneAndLabelNameEvaluationFlags, sceneAndLabelNameEvaluationFlags );
					scene = GetScene( fs, arguments[0].AsVariableName() );
					lineNumber = ( arguments.Count >= 2 ) ? scene.GetLabelLineNumber( arguments[1].AsVariableName() ) : -1;
					JSONArray param = new JSONArray( ( arguments.Count > 2 ) ? arguments.GetRange( 2, arguments.Count - 2 ).ConvertAll( ( token ) => (JSONNode) token ) : new List<JSONNode>() );
					saveData["temps"] = new JSONObject();
					SetVariable( "param", param, true );
				}
				else if( command == "return" )
				{
					if( saveData["temps"]["choice_substack"].Count > 0 )
					{
						JSONNode stackEntry = saveData["temps"]["choice_substack"].Remove( saveData["temps"]["choice_substack"].Count - 1 );
						lineNumber = stackEntry["lineNum"];
					}
					else if( saveData["stats"]["choice_subscene_stack"].Count > 0 )
					{
						JSONNode stackEntry = saveData["stats"]["choice_subscene_stack"].Remove( saveData["stats"]["choice_subscene_stack"].Count - 1 );
						scene = GetScene( fs, stackEntry["name"] );
						lineNumber = stackEntry["lineNum"] - 1;
						saveData["temps"] = stackEntry["temps"];
					}
					else
					{
						LogError( "Reached '*return' command but the stack is empty!" );
						break;
					}
				}
				else if( command == "image" || command == "text_image" || command == "youtube" || command == "link" || command == "link_button" )
					anyPageContentEncountered = true;
				else if( command == "print" )
				{
					// This command is also useful for logging the output of ChoiceScript expressions in the editor (mainly for testing purposes)
					int index = command.Length + 1;
					Debug.LogWarningFormat( "'{0}' = {1}", line, EvaluateExpression( line, ref index ) );
					anyPageContentEncountered = true;
				}
				else if( command == "achievement" || command == "scene_list" )
				{
					// Skip these metadata commands so that they won't affect 'anyPageContentEncountered'
					int indentation = CalculateLineIndentation( lineNumber );
					while( lineNumber < scene.Lines.Length - 1 && ( IsLineWhitespace( lineNumber + 1 ) || CalculateLineIndentation( lineNumber + 1 ) > indentation ) )
						lineNumber++;
				}
				else if( command == "page_break" || command == "page_break_advertisement" )
				{
					// "*page_break" will show a "Next" button if the page isn't empty
					if( anyPageContentEncountered )
						break;
				}
				else if( command == "restart" )
				{
					saveData["stats"].Clear();
					saveData["temps"].Clear();

					scene = GetScene( fs, "startup" );
					lineNumber = -1;
					anyPageContentEncountered = false;
				}
				else if( command == "reset" )
					LogError( "*reset command isn't supported by ChoiceExplorer." );
				else if( command == "delay_break" || command == "goto_random_scene" || command == "ending" || command == "delay_ending" || command == "abort" || command == "restore_game" )
					break;
			}

			if( lineNumber < scene.Lines.Length && topLineNumber < 0 )
			{
				AppendLineToResult( scene.Lines[lineNumber] + " <b>(No choice found...)</b>", result );
				topLineNumber = bottomLineNumber = lineNumber;
			}
		}

		private void ParseGosubCommand( string command, string line, out int lineNumber, out List<Token> arguments )
		{
			/// To learn why <see cref="Token.AsVariableName"/> is used for "goto" and "gosub" commands, see the comment inside "goto_scene".
			/// ---
			/// For "goto" command, the label name is the whole line after the command name.
			/// For "gosub" command, the label name is the first word after the command name.
			/// For "goto" and "gosub" commands, if the label name contains '{' or '[' characters, then it's evaluated as a standard token by ChoiceScript.
			/// Otherwise, its value is assigned as is (i.e. "goto label+1" will set the label to "label+1").
			/// For "gotoref" command, label name is always evaluated as a standard token by ChoiceScript. However, if the evaluated result is a string that
			/// contains '{' or '[' characters, then that string is evaluated once again (double evaluation!).
			string label;
			int index = command.Length + 1;
			if( command == "goto" )
			{
				arguments = null;
				label = line.Substring( index ).Trim();
			}
			else if( command == "gotoref" )
			{
				arguments = null;
				label = EvaluateExpression( line, ref index ).AsString();
			}
			else
			{
				arguments = GetCommandArguments( line, EvaluateFlags.StopAtFirstWord );
				label = arguments[0].AsVariableName();
			}

			if( label.IndexOf( '{' ) >= 0 || label.IndexOf( '[' ) >= 0 )
			{
				index = 0;
				label = EvaluateExpression( label, ref index ).AsVariableName();
			}

			lineNumber = scene.GetLabelLineNumber( label );
		}

		private string[] ExtractLines( int startLineNumber, int lineCount )
		{
			List<string> result = new List<string>( 2 );
			try
			{
				using( FileStream fs = new FileStream( gameAsarFilePath, FileMode.Open, FileAccess.Read ) )
				{
					sb.Length = 0;

					for( int i = 0; i < lineCount; i++ )
						AppendLineToResult( scene.Lines[startLineNumber + i], result );
				}
			}
			catch( Exception e )
			{
				LogError( "Critical error while extracting lines: {0}", e );
			}

			return PostProcessExtractedResults( result );
		}

		private string[] PostProcessExtractedResults( List<string> result )
		{
			if( sb.Length > 0 )
			{
				if( sb[sb.Length - 1] == '\n' )
					sb.Remove( sb.Length - 1, 1 );

				result.Add( sb.ToString() );
			}

			return result.ToArray();
		}

		private void AppendLineToResult( string line, List<string> result )
		{
			OnCurrentLineChanged( line );

			string command = GetCommandName( line );
			string lineColor = null;
			if( IsLineChoiceOption( line ) )
				lineColor = ChoiceLineColor;
			else if( command != null )
			{
				if( command == "if" || command == "elseif" || command == "elsif" || command == "else" )
					lineColor = ConditionLineColor;
				else if( command == "set" || command == "setref" || command == "config" || command == "temp" || command == "create" || command == "temp_array" || command == "create_array" )
					lineColor = VariableSetterLineColor;
				else
					lineColor = OtherCommandLineColor;
			}

			if( lineColor != null )
				sb.Append( "<color=" ).Append( lineColor ).Append( '>' );

			if( command != null && highlightVariablesInCommands.Contains( command ) )
			{
				int index = line.IndexOf( command ) + command.Length + 1;
				sb.Append( line, 0, index );

				bool insideString = false;
				int insideInlineExpression = 0;
				for( ; index < line.Length; index++ )
				{
					char ch = line[index];
					sb.Append( ch );

					if( ch == '"' )
						insideString = !insideString;
					else if( ch == '{' )
						insideInlineExpression++;
					else if( ch == '}' )
						insideInlineExpression--;
					else if( ch == '\\' ) // Skip escape sequences, e.g. '\"'
					{
						if( index < line.Length - 1 )
							sb.Append( line[++index] );
					}
					else if( ( !insideString || insideInlineExpression > 0 ) && char.IsLetter( ch ) )
					{
						int tokenStartIndex = index;
						while( index < line.Length - 1 && ( char.IsLetterOrDigit( line[index + 1] ) || line[index + 1] == '_' ) )
							sb.Append( line[++index] );

						object variableValue;
						if( TryGetVariable( line.Substring( tokenStartIndex, index - tokenStartIndex + 1 ), out variableValue ) && variableValue != null )
						{
							if( ( variableValue as string ) == "" )
								variableValue = "\"\""; // Display empty strings as ("") instead of () for clarity

							sb.Append( "<color=" ).Append( VariableValueColor ).Append( ">(" ).Append( variableValue ).Append( ")</color>" );
						}
					}
				}
			}
			else
				sb.Append( line );

			if( lineColor != null )
				sb.Append( "</color>" );

			if( sb.Length < MaxStringLength )
				sb.Append( '\n' );
			else
			{
				result.Add( sb.ToString() );
				sb.Length = 0;
			}
		}

		private string OnCurrentLineChanged( string line )
		{
			currentLine = line;
			parsedStringDepth = 0; // Normally, this isn't necessary. However, if an exception occurs at the previous line, this value may not be reset automatically.

			return line;
		}

		private bool EvaluateCondition( string line, int lineNumber, int index )
		{
			SaveManager.LogVerbose( "<b>=== Evaluating condition {0}: {1}</b>", lineNumber, line );
			bool result = EvaluateExpression( line, ref index ).AsBool();
			SaveManager.LogVerbose( "<b>=== Evaluated condition {0}</b>", result );
			return result;
		}

		private Token EvaluateFunction( string line, ref int index, int indexStartOffset = 0, EvaluateFlags flags = EvaluateFlags.None )
		{
			// Ignore whitespace before the opening parenthesis
			index += indexStartOffset;
			while( char.IsWhiteSpace( line[index] ) )
				index++;

			Assertion( line[index] == '(', "Expected '(', found '{0}' at {1}", line[index], index );
			Token result = EvaluateExpression( line, ref index, 1, FlagsForBlockEvaluation( flags ) );
			Assertion( line[index] == ')', "Expected ')', found '{0}' at {1}", line[index], index );

			return result;
		}

		/// <param name="implicitVariable">
		/// If a <see cref="Token"/> (e.g. variable) isn't found before an operator (e.g. '+', '-'), this variable will act as the left-hand side of the arithmetic operation.
		/// Otherwise, this variable will silently be overwritten by the first found <see cref="Token"/>.
		/// </param>
		private Token EvaluateExpression( string line, ref int index, int indexStartOffset = 0, EvaluateFlags flags = EvaluateFlags.None, string implicitVariable = null )
		{
			Token currValue = ( implicitVariable == null ) ? Token.Null() : Token.Variable( implicitVariable );
			index += indexStartOffset;

			try
			{
				if( index < line.Length )
					SaveManager.LogVerbose( "Start: [{0}]='{1}'", index, line[index] );

				for( ; index < line.Length; index++ )
				{
					char ch = line[index];
					if( char.IsWhiteSpace( ch ) )
					{
						if( ( flags & EvaluateFlags.StopAtWhitespace ) == EvaluateFlags.StopAtWhitespace && currValue.Type != TokenType.None )
							break;

						continue;
					}

					if( ( flags & EvaluateFlags.StopAtFirstToken ) == EvaluateFlags.StopAtFirstToken && currValue.Type != TokenType.None )
					{
						if( ch != '[' ) // "my_array[index]" is considered a single token by ChoiceScript
							return currValue;
					}

					if( ( flags & EvaluateFlags.StopAtFirstWord ) == EvaluateFlags.StopAtFirstWord )
					{
						int wordStartIndex = index++;
						while( index < line.Length && !char.IsWhiteSpace( line[index] ) )
							index++;

						string word = line.Substring( wordStartIndex, index - wordStartIndex );
						SaveManager.LogVerbose( "Read word '{0}' at {1}-{2}", word, wordStartIndex, index - 1 );

						return Token.String( word );
					}

					if( ch == '"' )
					{
						int stringStartIndex = index;
						parsedStringDepth++;
						string value = ParseString( line, ref index, 1, '"' );
						parsedStringDepth--;

						SaveManager.LogVerbose( "Read string '{0}' at {1}-{2}", value, stringStartIndex, index );

						Assertion( line[index] == '"', "Expected '\"', found '{0}' at {1}", line[index], index );
						Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before string '{0}' at {1}, found {2}", value, stringStartIndex, currValue );
						currValue = Token.String( value );
					}
					else if( char.IsLetterOrDigit( ch ) )
					{
						int tokenStartIndex = index;
						while( index < line.Length - 1 && ( char.IsLetterOrDigit( line[index + 1] ) || line[index + 1] == '_' || line[index + 1] == '.' ) )
							index++;

						string token = line.Substring( tokenStartIndex, index - tokenStartIndex + 1 );
						SaveManager.LogVerbose( "Read token '{0}' at {1}-{2}", token, tokenStartIndex, index );

						if( token == "not" )
						{
							Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before token '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
							currValue = Token.Bool( !EvaluateFunction( line, ref index, 1, flags ).AsBool() );
						}
						else if( token == "and" )
						{
							Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", token, tokenStartIndex );
							return Token.Bool( currValue.AsBool() & EvaluateExpression( line, ref index, 1, flags ).AsBool() ); // We use '&' instead of '&&' because we MUST evaluate the right-hand side and not early exit
						}
						else if( token == "or" )
						{
							Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", token, tokenStartIndex );
							return Token.Bool( currValue.AsBool() | EvaluateExpression( line, ref index, 1, flags ).AsBool() ); // We use '|' instead of '||' because we MUST evaluate the right-hand side and not early exit
						}
						else if( token == "modulo" )
						{
							Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", token, tokenStartIndex );
							return Token.Number( currValue.AsDouble() % EvaluateExpression( line, ref index, 1, flags ).AsDouble() );
						}
						else if( token == "log" )
						{
							Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before function '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
							currValue = Token.Number( Math.Log( EvaluateFunction( line, ref index, 1, flags ).AsDouble(), 10.0 ) );
						}
						else if( token == "length" )
						{
							Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before function '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
							currValue = Token.Number( EvaluateFunction( line, ref index, 1, flags ).AsString().Length );
						}
						else if( token == "round" )
						{
							Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before function '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
							currValue = Token.Number( Math.Round( EvaluateFunction( line, ref index, 1, flags ).AsDouble(), MidpointRounding.AwayFromZero ) );
						}
						else if( token == "timestamp" ) // Seconds since epoch (January 1, 1970)
						{
							Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before function '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
							currValue = Token.Number( ( DateTime.Parse( EvaluateFunction( line, ref index, 1, flags ).AsString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeLocal ) - new DateTime( 1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc ) ).TotalSeconds );
						}
						else if( token == "true" || token == "false" )
						{
							Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before boolean '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
							currValue = Token.Bool( token == "true" );
						}
						else
						{
							/// If the value is e.g. '123', it's a <see cref="TokenType.Number"/>. If the value is e.g. '1chapter', it's a <see cref="TokenType.Variable"/>. Although the variable's name
							/// must start with a letter, a scene name can start with a number and we need to be able to capture '1chapter' from '*goto_scene 1chapter' and this is the way to do it for me.
							double number;
							if( char.IsDigit( token[0] ) && double.TryParse( token, NumberStyles.Float, CultureInfo.InvariantCulture, out number ) )
							{
								Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before number '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
								currValue = Token.Number( number );
							}
							else
							{
								Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before variable '{0}' at {1}, found {2}", token, tokenStartIndex, currValue );
								currValue = Token.Variable( token );
							}
						}
					}
					else if( ch == '{' )
					{
						int startIndex = index;
						SaveManager.LogVerbose( "Entered ref variable at {0}", startIndex );
						Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before token '{0}' at {1}, found {2}", ch, index, currValue );

						currValue = EvaluateExpression( line, ref index, 1, FlagsForBlockEvaluation( flags ) ).UnpackVariable();
						if( currValue.Type == TokenType.String )
						{
							/// Sometimes, a number variable can be saved as string by ChoiceScript (e.g. "5" instead of 5). When these variables are accessed as ref variables,
							/// we need to convert them to number tokens because it's impossible to have a variable with a name consisting of digits only (e.g. "5"). This issue
							/// occurs in "Professor of Magical Studies" where most number variables are saved as strings for unknown reasons.
							double number;
							if( double.TryParse( currValue.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number ) )
								currValue = Token.Number( number );
							else
								currValue = Token.Variable( currValue.AsString() );
						}

						SaveManager.LogVerbose( "Evaluated ref variable '{0}' with result {1}'", line.Substring( startIndex, index - startIndex + 1 ), currValue );
						Assertion( line[index] == '}', "Expected '}}', found '{0}' at '{1}'", line[index], index ); // '}}' is intentional, it's escaped (otherwise string.Format throws FormatException)
					}
					else if( ch == '[' )
					{
						int arrayStartIndex = index;
						SaveManager.LogVerbose( "Entered array at {0}", arrayStartIndex );
						Assertion( currValue.Type == TokenType.Variable, "'{0}' at {1} was expected to be a Variable", currValue, index );

						string arrayIndex = EvaluateExpression( line, ref index, 1, FlagsForBlockEvaluation( flags ) ).AsString();
						currValue = Token.Variable( currValue.AsVariableName() + "_" + arrayIndex );

						SaveManager.LogVerbose( "Evaluated array '{0}' with result {1} ({2})'", line.Substring( arrayStartIndex, index - arrayStartIndex + 1 ), arrayIndex, currValue.Value );
						Assertion( line[index] == ']', "Expected ']', found '{0}' at '{1}'", line[index], index );
					}
					else if( ch == '(' )
					{
						int startIndex = index;
						SaveManager.LogVerbose( "Entered parentheses at {0}", startIndex );
						Assertion( currValue.Type == TokenType.None || implicitVariable != null, "No value was expected before token '{0}' at {1}, found {2}", ch, index, currValue );

						currValue = EvaluateFunction( line, ref index, 0, flags );

						SaveManager.LogVerbose( "Evaluated parentheses '{0}' with result {1}'", line.Substring( startIndex, index - startIndex + 1 ), currValue );
					}
					else if( ch == ')' || ch == ']' || ch == '}' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return currValue;
					}
					else if( ch == '=' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return Token.Bool( currValue.ValueEquals( EvaluateExpression( line, ref index, 1, flags ) ) );
					}
					else if( ch == '!' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						Assertion( line[index + 1] == '=', "Unexpected token '!{0}' at {1}", line[index + 1], index );
						return Token.Bool( !currValue.ValueEquals( EvaluateExpression( line, ref index, 2, flags ) ) );
					}
					else if( ch == '<' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						if( line[index + 1] == '=' )
							return Token.Bool( currValue.AsDouble() <= EvaluateExpression( line, ref index, 2, flags ).AsDouble() );
						else
							return Token.Bool( currValue.AsDouble() < EvaluateExpression( line, ref index, 1, flags ).AsDouble() );
					}
					else if( ch == '>' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						if( line[index + 1] == '=' )
							return Token.Bool( currValue.AsDouble() >= EvaluateExpression( line, ref index, 2, flags ).AsDouble() );
						else
							return Token.Bool( currValue.AsDouble() > EvaluateExpression( line, ref index, 1, flags ).AsDouble() );
					}
					else if( ch == '+' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return Token.Number( currValue.AsDouble() + EvaluateExpression( line, ref index, 1, flags ).AsDouble() );
					}
					else if( ch == '-' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return Token.Number( currValue.AsDouble() - EvaluateExpression( line, ref index, 1, flags ).AsDouble() );
					}
					else if( ch == '*' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return Token.Number( currValue.AsDouble() * EvaluateExpression( line, ref index, 1, flags ).AsDouble() );
					}
					else if( ch == '/' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return Token.Number( currValue.AsDouble() / EvaluateExpression( line, ref index, 1, flags ).AsDouble() );
					}
					else if( ch == '^' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return Token.Number( Math.Pow( currValue.AsDouble(), EvaluateExpression( line, ref index, 1, flags ).AsDouble() ) );
					}
					else if( ch == '&' )
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before token '{0}' at {1}", ch, index );
						return Token.String( currValue.AsString() + EvaluateExpression( line, ref index, 1, flags ).AsString() );
					}
					else if( ch == '%' ) // Fairmath: https://choicescriptdev.fandom.com/wiki/Arithmetic_operators#Fairmath
					{
						ch = line[index + 1];
						Assertion( currValue.Type != TokenType.None, "A value was expected before fairmath '%{0}' at {1}", ch, index );

						double number = currValue.AsDouble();
						double percentage = EvaluateExpression( line, ref index, 2, flags ).AsDouble() / 100.0;

						if( ch == '+' )
							return Token.Number( Math.Min( 99.0, Math.Floor( number + ( 100.0 - number ) * percentage ) ) );
						else if( ch == '-' )
							return Token.Number( Math.Max( 1.0, Math.Ceiling( number - number * percentage ) ) );
						else
							LogError( "Unexpected token '%{0}' at {1}", ch, index );
					}
					else if( ch == '#' ) // CharAt
					{
						Assertion( currValue.Type != TokenType.None, "A value was expected before CharAt '{0}' at {1}", ch, index );
						int charIndex = EvaluateExpression( line, ref index, 1, flags ).AsInt() - 1;
						SaveManager.LogVerbose( "Retrieving char at index {0} from {1}", charIndex, currValue.Value );
						return Token.String( currValue.AsString()[charIndex].ToString() );
					}
					else if( ch == '\\' )
					{
						// Don't output an error if this is a string escape character ('\') even though we aren't inside a string because unlike ChoiceScript (which
						// unescapes strings as a whole before evaluating them; see 'Scene.prototype.evaluateValueToken'), our parser unescapes strings on the run.
						while( line[index + 1] == '\\' )
							index++;

						Assertion( line[index + 1] == '"', "Unexpected escape sequence '\\{0}' at {1}", line[index + 1], index );
					}
					else
						LogError( "Unexpected character '{0}' at {1}", ch, index );
				}

				Assertion( currValue.Type != TokenType.None, "A value was expected before end of line at {0}", index );
				return currValue;
			}
			catch( Exception e )
			{
				LogError( "Exception at index {0} ({1}): {2}", index, currValue, e );
				return Token.Bool( false );
			}
			finally
			{
				SaveManager.LogVerbose( "End: [{0}]='{1}' with {2}", index, line[Mathf.Min( index, line.Length - 1 )], currValue );
			}
		}

		private string ParseString( string line, ref int index, int indexStartOffset = 0, params char[] terminatorChars )
		{
			StringBuilder sb = new StringBuilder( line.Length );
			for( index += indexStartOffset; index < line.Length; index++ )
			{
				char ch = line[index];
				if( terminatorChars != null && Array.IndexOf( terminatorChars, ch ) >= 0 )
					break;
				else if( ch == '\\' ) // Unescape string
				{
					// Number of backslashes required for escaping characters inside the string: "(2 ^ depth) - 1" (we subtract 0.5 and then cast to int to avoid floating point imprecision issues)
					// Number of backslashes required before start/end quotation marks: "(Nested string backslash count) / 2" (integer division ignores the fraction)
					// Writing "Hello" (with quotation marks) inside a string:
					// 0th depth (plaintext): "Hello"
					// 1st depth: " \"Hello\" "
					// 2nd depth: \" \\\"Hello\\\" \"
					// 3rd depth: \\\" \\\\\\\"Hello\\\\\\\" \\\"
					// 4th depth: :-)
					int escapeBackslashes = (int) ( Mathf.Pow( 2, parsedStringDepth ) - 0.5f );
					int endingBackslashes = escapeBackslashes / 2;
					for( ; --escapeBackslashes >= 0; index++ )
					{
						Assertion( line[index] == '\\', "Expected '\\', found '{0}' at {1}", line[index], index );
						if( --endingBackslashes == 0 && line[index + 1] == '"' )
							break;
					}

					if( escapeBackslashes < 0 ) // Otherwise, we've encountered the ending quotation mark
					{
						// There are no special conditions like '\n', we simply ignore the '\' character. So '\n' will be printed as 'n'. This is how it's done in ChoiceScript.
						sb.Append( line[index] );
					}
				}
				else if( line.ContainsAt( "${", index ) || line.ContainsAt( "$!{", index ) || line.ContainsAt( "$!!{", index ) ) // Inline expression
				{
					int prefixLength = line.IndexOf( '{', index ) - index + 1;
					string value = EvaluateExpression( line, ref index, prefixLength ).AsString();
					Assertion( line[index] == '}', "Expected '}}', found '{0}' at {1}", line[index], index ); // '}}' is intentional, it's escaped (otherwise string.Format throws FormatException)
					sb.Append( ( prefixLength <= 2 ) ? value : CapitalizeString( value, prefixLength == 4 ) );
				}
				else if( line.ContainsAt( "@{", index ) || line.ContainsAt( "@!{", index ) || line.ContainsAt( "@!!{", index ) ) // Multireplace
				{
					/// Multireplace allows the first token to be and expression without parentheses as long as there's no whitespace between characters (e.g. 'variable+1').
					/// Which is why we're evaluating the expression with flag <see cref="EvaluateFlags.StopAtWhitespace"/>.
					int prefixLength = line.IndexOf( '{', index ) - index + 1;
					Token token = EvaluateExpression( line, ref index, prefixLength, EvaluateFlags.StopAtWhitespace );
					int valueIndex = ( token.Type == TokenType.Bool ) ? ( token.AsBool() ? 1 : 2 ) : token.AsInt();
					string value = null;
					do
					{
						string _value = ParseString( line, ref index, 1, '|', '}' );
						if( --valueIndex == 0 )
							value = _value;
					} while( line[index] != '}' );

					Assertion( line[index] == '}', "Expected '}}', found '{0}' at {1}", line[index], index ); // '}}' is intentional, it's escaped (otherwise string.Format throws FormatException)
					sb.Append( ( prefixLength <= 2 ) ? value : CapitalizeString( value, prefixLength == 4 ) );
				}
				else if( line.ContainsAt( "[n/]", index ) )
				{
					sb.Append( '\n' );
					index += 3;
				}
				else
					sb.Append( ch );
			}

			return sb.ToString();
		}

		private EvaluateFlags FlagsForBlockEvaluation( EvaluateFlags flags )
		{
			// These flags must be ignored inside blocks (e.g. '()', '{}', '[]')
			flags &= ~( EvaluateFlags.StopAtFirstToken | EvaluateFlags.StopAtWhitespace );
			return flags;
		}

		private static T GetVariable<T>( string variableName )
		{
			T result;
			if( !TryGetVariable( variableName, out result ) )
				LogError( "Variable couldn't be found: '{0}'", variableName );

			return result;
		}

		private static bool TryGetVariable<T>( string variableName, out T variableValue )
		{
			JSONNode value;
			if( saveData["stats"].TryGetValue( variableName, out value ) || saveData["temps"].TryGetValue( variableName, out value ) )
			{
				if( value.RawValue is T )
				{
					variableValue = (T) value.RawValue;
					return true;
				}
				else
				{
					try
					{
						variableValue = (T) Convert.ChangeType( value.RawValue, typeof( T ), CultureInfo.InvariantCulture );
						return true;
					}
					catch
					{
						LogError( "Couldn't convert variable '{0}'s value from '{1}' ({2}) to {3}", variableName, value.RawValue, ( value.RawValue != null ) ? value.RawValue.GetType().Name : "Null", typeof( T ).Name );
						variableValue = default( T );
						return true;
					}
				}
			}

			// Assume that internal ChoiceScript variables always exist (e.g. achievement variables "choice_achieved...", DLC purchase variables "choice_purchased", etc.)
			if( variableName.StartsWith( "choice_", StringComparison.OrdinalIgnoreCase ) )
			{
				if( variableName.StartsWith( "choice_purchase", StringComparison.OrdinalIgnoreCase ) && typeof( T ).IsAssignableFrom( typeof( bool ) ) )
					variableValue = (T) (object) true; // Assume everything is purchased
				else
					variableValue = default( T );

				return true;
			}

			// At the very beginning of the game, when importing a save from the previous installment of the series, the stats that 
			// exist in the new installment but not in the previous installment aren't saved to ["stats"] by ChoiceScript. But they
			// can be retrieved from ["undeleted"]["startingStats"]. Merge these stats and try again.
			if( !mergedPreviousBooksStats )
			{
				mergedPreviousBooksStats = true;

				if( saveData.TryGetValue( "undeleted", out value ) && value.TryGetValue( "startingStats", out value ) )
				{
					SaveManager.LogVerbose( "Merging previous installment's stats with current stats" );

					foreach( KeyValuePair<string, JSONNode> kvPair in value )
					{
						if( !saveData["stats"].HasKey( kvPair.Key ) )
							saveData["stats"][kvPair.Key] = kvPair.Value;
					}
				}

				return TryGetVariable( variableName, out variableValue );
			}

			variableValue = default( T );
			return false;
		}

		private static void SetVariable( string variableName, JSONNode value, bool? isTempVariable = null )
		{
			variableName = variableName.ToLowerInvariant();
			saveData[( isTempVariable ?? !saveData["stats"].HasKey( variableName ) ) ? "temps" : "stats"][variableName] = value;
		}

		private int CalculateLineIndentation( int lineNumber )
		{
			return CalculateLineIndentation( scene.Lines[lineNumber] );
		}

		private int CalculateLineIndentation( string line )
		{
			int i = 0;
			while( i < line.Length && char.IsWhiteSpace( line[i] ) )
				i++;

			return i;
		}

		private bool IsLineWhitespace( int lineNumber )
		{
			string line = scene.Lines[lineNumber];
			for( int i = 0; i < line.Length; i++ )
			{
				if( !char.IsWhiteSpace( line[i] ) )
					return line.ContainsAt( "*comment", i ); // Consider "*comment" lines whitespace
			}

			return true;
		}

		private bool IsLineChoiceOption( string line )
		{
			// The idea here is simple: if '#' doesn't belong to a string and the line isn't a comment or plaintext, then it's a choice option.
			// Example valid line:
			// *if (variable = "Hello \"friend\"") #Option
			// Example invalid line:
			// Then the ATM showed #### in the PIN section.
			int index = 0;
			while( index < line.Length && char.IsWhiteSpace( line[index] ) )
				index++;

			if( index >= line.Length - 1 ) // End of line is reached (if last character is '#', this is still not a choice option because the choice has no label in that case)
				return false;
			else if( line[index] == '#' ) // A guaranteed choice option
				return true;

			// If the command isn't one of the few ones that can precede choice options, skip it
			string command = GetCommandName( line );
			if( command != "if" && command != "selectable_if" && command != "hide_reuse" && command != "disable_reuse" && command != "allow_reuse" )
				return false;

			bool insideString = false;
			int insideParentheses = 0;
			for( index++; index < line.Length; index++ )
			{
				char ch = line[index];
				if( ch == '"' )
					insideString = !insideString;
				else if( ch == '\\' ) // Skip escape sequences, e.g. '\"'
					index++;
				else if( !insideString )
				{
					if( ch == '(' || ch == '{' || ch == '[' )
						insideParentheses++;
					else if( ch == ')' || ch == '}' || ch == ']' )
						insideParentheses--;
					else if( ch == '#' && insideParentheses == 0 ) // If we encounter '#' that is outside of any parentheses and strings, then this is a choice option
						return true;
				}
			}

			return false;
		}

		/// <returns>The command name without the preceding <c>*</c> character, or <c>null</c> if this line isn't a command.</returns>
		private string GetCommandName( string line )
		{
			int startIndex, endIndex;
			GetCommandNameStartEndIndices( line, out startIndex, out endIndex );
			return ( startIndex >= 0 ) ? line.Substring( startIndex, endIndex - startIndex ) : null;
		}

		/// <summary>
		/// Returns indices in [<paramref name="startIndex"/>, <paramref name="endIndex"/>) format. So, if the command is "<c>*if (...)</c>",
		/// <paramref name="startIndex"/> will be 1 while <paramref name="endIndex"/> will be 3.
		/// </summary>
		private void GetCommandNameStartEndIndices( string line, out int startIndex, out int endIndex )
		{
			int index = 0;
			while( index < line.Length && char.IsWhiteSpace( line[index] ) )
				index++;

			if( index >= line.Length - 1 || line[index] != '*' )
			{
				startIndex = endIndex = -1;
				return;
			}

			startIndex = ++index;
			while( index < line.Length && ( char.IsLetterOrDigit( line[index] ) || line[index] == '_' ) )
				index++;

			endIndex = index;
		}

		private List<Token> GetCommandArguments( string line, params EvaluateFlags[] perArgumentFlags )
		{
			int startIndex, index;
			GetCommandNameStartEndIndices( line, out startIndex, out index );
			if( startIndex < 0 )
				return new List<Token>( 0 );

			List<Token> result = new List<Token>( 4 );
			while( index < line.Length )
			{
				Token token = EvaluateExpression( line, ref index, 0, ( perArgumentFlags != null && result.Count < perArgumentFlags.Length ) ? perArgumentFlags[result.Count] : EvaluateFlags.StopAtFirstToken );
				if( token.Type != TokenType.None )
					result.Add( token );
			}

			return result;
		}

		/// <param name="allLetters">If <c>true</c>, all letters are capitalized. Otherwise, only the first letter is capitalized.</param>
		private string CapitalizeString( string value, bool allLetters )
		{
			if( allLetters || value.Length == 1 )
				return value.ToUpperInvariant();
			else
				return char.ToUpperInvariant( value[0] ) + value.Substring( 1 );
		}

		#region Logging
		private static void Assertion( bool condition, string message )
		{
			if( !condition )
				LogError( message );
		}

		private static void Assertion( bool condition, string format, object arg0 )
		{
			if( !condition )
				LogError( format, arg0 );
		}

		private static void Assertion( bool condition, string format, object arg0, object arg1 )
		{
			if( !condition )
				LogError( format, arg0, arg1 );
		}

		private static void Assertion( bool condition, string format, object arg0, object arg1, object arg2 )
		{
			if( !condition )
				LogError( format, arg0, arg1, arg2 );
		}

		private static void Assertion( bool condition, string format, params object[] args )
		{
			if( !condition )
				LogError( format, args );
		}

		private static void LogError( string format, object arg0 )
		{
			LogError( string.Format( format, arg0 ) );
		}

		private static void LogError( string format, object arg0, object arg1 )
		{
			LogError( string.Format( format, arg0, arg1 ) );
		}

		private static void LogError( string format, object arg0, object arg1, object arg2 )
		{
			LogError( string.Format( format, arg0, arg1, arg2 ) );
		}

		private static void LogError( string format, params object[] args )
		{
			LogError( string.Format( format, args ) );
		}

		private static void LogError( string message )
		{
			if( lastErrorLine != currentLine && !string.IsNullOrEmpty( currentLine ) )
			{
				string errorLineMessage = string.Format( "<b>Error(s) while evaluating: {0}</b>", currentLine.Trim() );
				Debug.Assert( false, errorLineMessage );
				sb.Append( "\n" ).Append( errorLineMessage ).Append( "\n" );
				lastErrorLine = currentLine;
			}

			/// <see cref="Debug.Assert"/> doesn't show the <see cref="ErrorReporter"/>. It's good because the errors are already logged to the text output.
			Debug.Assert( false, message );
			sb.Append( "<b>" ).Append( message ).Append( "</b>\n" );

#if UNITY_EDITOR
			// Audio clue that something got wrong
			UnityEditor.EditorApplication.Beep();
#endif
		}
		#endregion
	}
}