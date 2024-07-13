using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public partial class ChoiceExplorerWindow
	{
#pragma warning disable 0649
		/// <summary>
		/// Highlights a single line.
		/// </summary>
		[SerializeField]
		private Image lineHighlight;
		private Coroutine lineHighlightCoroutine;

		/// <summary>
		/// Highlights the expanded height of a line (e.g. indicates where an "*if" command starts and ends).
		/// </summary>
		[SerializeField]
		private RectTransform indentationHighlight;
		private Text indentationHighlightStartEntry, indentationHighlightEndEntry;
		private int indentationHighlightStartChar, indentationHighlightEndChar;
		private int indentationHighlightEndGlobalLineNumber;
#pragma warning restore 0649

		private IEnumerator HighlightLineCoroutine( Vector2 linePosition )
		{
			lineHighlight.SetOpacity( 1f );

			Vector2 initialScrollPosition = scrollView.content.anchoredPosition;
			Vector2 scrollPosition = scrollView.ClampScrollPosition( new Vector2( 0f, -linePosition.y - scrollView.viewport.rect.height * 0.5f ) );

			float scrollSpeed = 1f / Mathf.Clamp( Vector2.Distance( initialScrollPosition, scrollPosition ) / 1000f, 0.001f, 1f );
			for( float t = 0f; t < 1f; t += Time.unscaledDeltaTime * scrollSpeed )
			{
				scrollView.content.anchoredPosition = Vector2.LerpUnclamped( initialScrollPosition, scrollPosition, 1f - ( 1f - t ) * ( 1f - t ) ); // OutQuad easing
				yield return null;
			}

			scrollView.content.anchoredPosition = scrollPosition;

			for( float t = 0f; t < 1f; t += Time.unscaledDeltaTime )
			{
				lineHighlight.SetOpacity( 1f - t * t ); // InQuad easing
				yield return null;
			}

			lineHighlight.SetOpacity( 0f );
			lineHighlightCoroutine = null;
		}

		private void RefreshIndentationHighlight()
		{
			if( indentationHighlightStartEntry == null )
			{
				indentationHighlight.anchoredPosition = new Vector2( -1000f, 0f );
				return;
			}

			EnsureEntryTextGeneratorUpToDate( indentationHighlightStartEntry );

			Text endEntry = indentationHighlightEndEntry;
			int endLocalLineNumber;
			if( endEntry == null )
			{
				endLocalLineNumber = ConvertGlobalLineNumberToLocalLineNumber( indentationHighlightEndGlobalLineNumber, out endEntry );

				// If the indentation highlight ends inside a currently visible Text entry, cache it for improved performance. Otherwise,
				// it'll be cached when user loads more lines at the bottom
				if( endLocalLineNumber < endEntry.cachedTextGenerator.lines.Count - 1 )
				{
					indentationHighlightEndEntry = endEntry;
					indentationHighlightEndChar = endEntry.cachedTextGenerator.lines[endLocalLineNumber].startCharIdx;
				}
			}
			else
			{
				EnsureEntryTextGeneratorUpToDate( endEntry );
				endLocalLineNumber = ( endEntry.cachedTextGenerator.lines as List<UILineInfo> ).FindIndex( ( e ) => e.startCharIdx == indentationHighlightEndChar );
			}

			Vector2 top = ConvertLocalEntryPositionToGlobal( indentationHighlightStartEntry, indentationHighlightStartEntry.cachedTextGenerator.verts[indentationHighlightStartChar * 4].position );
			Vector2 bottom = ConvertLocalEntryPositionToGlobal( endEntry, new Vector2( 0f, endEntry.cachedTextGenerator.lines[endLocalLineNumber].topY ) );
			indentationHighlight.anchoredPosition = top;
			indentationHighlight.sizeDelta = new Vector2( indentationHighlight.sizeDelta.x, top.y - bottom.y );
		}

		private void OnEntryClicked( PointerEventData eventData )
		{
			if( eventData.clickCount != 2 )
				return;

			try
			{
				Text entry;
				int localLineNumber, globalLineNumber;
				if( !TryGetLineNumberAtScreenPosition( eventData.position, out entry, out localLineNumber, out globalLineNumber ) || IsLineWhitespace( globalLineNumber ) )
				{
					indentationHighlightStartEntry = indentationHighlightEndEntry = null;
					return;
				}

				string command = GetCommandName( scene.Lines[globalLineNumber] );
				if( command == "goto" || command == "gosub" || command == "gotoref" ) // Jump to clicked label
				{
					List<Token> arguments;
					ParseGosubCommand( command, scene.Lines[globalLineNumber].Trim(), out globalLineNumber, out arguments );

					if( globalLineNumber < topLineNumber )
						LoadMoreLines( false, topLineNumber - globalLineNumber );
					else if( globalLineNumber > bottomLineNumber )
						LoadMoreLines( true, globalLineNumber - bottomLineNumber + LoadMoreLinesCount );

					localLineNumber = ConvertGlobalLineNumberToLocalLineNumber( globalLineNumber, out entry );
					Vector2 linePosition = ConvertLocalEntryPositionToGlobal( entry, new Vector2( 0f, entry.cachedTextGenerator.lines[localLineNumber].topY ) );

					if( lineHighlightCoroutine != null )
						StopCoroutine( lineHighlightCoroutine );

					lineHighlight.rectTransform.anchoredPosition = new Vector2( lineHighlight.rectTransform.anchoredPosition.x, linePosition.y );
					lineHighlight.rectTransform.sizeDelta = new Vector2( lineHighlight.rectTransform.sizeDelta.x, entry.cachedTextGenerator.lines[localLineNumber].height / CanvasScale );
					lineHighlightCoroutine = StartCoroutine( HighlightLineCoroutine( linePosition ) );
				}
				else // Highlight the clicked command's indentation
				{
					int indentation = CalculateLineIndentation( globalLineNumber );
					int endGlobalLineNumber = globalLineNumber + 1;
					bool nonWhitespaceIntermediateLineEncountered = false;
					while( endGlobalLineNumber < scene.Lines.Length && ( IsLineWhitespace( endGlobalLineNumber ) || CalculateLineIndentation( endGlobalLineNumber ) > indentation ) )
					{
						nonWhitespaceIntermediateLineEncountered = nonWhitespaceIntermediateLineEncountered || !IsLineWhitespace( endGlobalLineNumber );
						endGlobalLineNumber++;
					}

					if( !nonWhitespaceIntermediateLineEncountered )
					{
						indentationHighlightStartEntry = indentationHighlightEndEntry = null;
						return;
					}

					int startChar = entry.cachedTextGenerator.lines[localLineNumber].startCharIdx;
					//int endChar = Mathf.Min( entry.text.Length, ( localLineNumber < entry.cachedTextGenerator.lines.Count - 1 ) ? entry.cachedTextGenerator.lines[localLineNumber + 1].startCharIdx : entry.cachedTextGenerator.characterCount );
					while( entry.text.ContainsAt( "<b>", startChar ) || entry.text.ContainsAt( "<color", startChar ) ) // Skip rich text tags
						startChar = entry.text.IndexOf( '>', startChar ) + 1;

					indentationHighlightStartEntry = entry;
					indentationHighlightStartChar = startChar + indentation;
					indentationHighlightEndEntry = null;
					indentationHighlightEndGlobalLineNumber = endGlobalLineNumber;
				}
			}
			finally
			{
				RefreshIndentationHighlight();
			}
		}

		/// <param name="entry">The <see cref="Text"/> entry that holds the line at the screen position.</param>
		/// <param name="localLineNumber">The line number inside the <see cref="Text"/> <paramref name="entry"/>. Wrapped lines count as additional lines.</param>
		/// <param name="globalLineNumber">The line number inside the current <see cref="SceneData.Lines"/>. There is no wrapped line concept here.</param>
		/// <returns><c>true</c>, if a line exists at screen position.</returns>
		private bool TryGetLineNumberAtScreenPosition( Vector2 screenPosition, out Text entry, out int localLineNumber, out int globalLineNumber )
		{
			localLineNumber = globalLineNumber = -1;
			entry = activeEntries.Find( ( e ) => RectTransformUtility.RectangleContainsScreenPoint( e.rectTransform, screenPosition, null ) );
			if( entry == null )
				return false;

			Vector2 localPoint;
			RectTransformUtility.ScreenPointToLocalPointInRectangle( entry.rectTransform, screenPosition, null, out localPoint );
			localPoint *= CanvasScale; /// <see cref="UILineInfo"/>'s properties are scaled with Canvas; counter it.

			string text = entry.text;
			List<UILineInfo> lineInfos = entry.cachedTextGenerator.lines as List<UILineInfo>;
			localLineNumber = lineInfos.FindIndex( ( e ) => localPoint.y >= e.topY - e.height );
			if( localLineNumber < 0 )
				return false;

			globalLineNumber = ConvertLocalLineNumberToGlobalLineNumber( ref localLineNumber, entry );
			return true;
		}

		/// <summary>
		/// Converts a <see cref="Text"/> entry's local position to a position relative to ScrollRect.
		/// </summary>
		private Vector2 ConvertLocalEntryPositionToGlobal( Text entry, Vector2 localPosition )
		{
			/// <see cref="UILineInfo"/>'s properties are scaled with Canvas; counter it.
			return entriesLayoutGroup.transform.InverseTransformPoint( entry.rectTransform.TransformPoint( localPosition / CanvasScale ) );
		}

		/// <param name="localLineNumber">The line number inside the <see cref="Text"/> <paramref name="entry"/>. Wrapped lines count as additional lines.</param>
		/// <param name="entry">The <see cref="Text"/> entry that holds the local line.</param>
		/// <returns>The line number inside the current <see cref="SceneData.Lines"/>. There is no wrapped line concept here.</returns>
		private int ConvertLocalLineNumberToGlobalLineNumber( ref int localLineNumber, Text entry )
		{
			EnsureEntryTextGeneratorUpToDate( entry );

			// Ignore wrapped lines
			IList<UILineInfo> lineInfos = entry.cachedTextGenerator.lines;
			while( localLineNumber > 0 && entry.text[lineInfos[localLineNumber].startCharIdx - 1] != '\n' )
				localLineNumber--;

			int result = topLineNumber;
			foreach( Text _entry in activeEntries )
			{
				EnsureEntryTextGeneratorUpToDate( _entry );
				lineInfos = _entry.cachedTextGenerator.lines;
				for( int i = 0; i < lineInfos.Count; i++ )
				{
					// Ignore wrapped lines
					int prevChar = lineInfos[i].startCharIdx - 1;
					if( prevChar >= 0 && _entry.text[prevChar] == '\n' )
						result++;

					if( i == localLineNumber && _entry == entry )
						return result;
				}

				result++;
			}

			Debug.LogErrorFormat( "Couldn't convert local line number {0} to global line number ({1})", localLineNumber, result );
			return -1;
		}

		/// <param name="globalLineNumber">The line number inside the current <see cref="SceneData.Lines"/>. There is no wrapped line concept here.</param>
		/// <param name="entry">The <see cref="Text"/> entry that holds the local line.</param>
		/// <returns>The line number inside the <see cref="Text"/> <paramref name="entry"/>. Wrapped lines count as additional lines.</returns>
		private int ConvertGlobalLineNumberToLocalLineNumber( int globalLineNumber, out Text entry )
		{
			if( globalLineNumber < topLineNumber )
			{
				entry = activeEntries[0];
				EnsureEntryTextGeneratorUpToDate( entry );
				return 0;
			}
			else if( globalLineNumber > bottomLineNumber )
			{
				entry = activeEntries[activeEntries.Count - 1];
				EnsureEntryTextGeneratorUpToDate( entry );
				return entry.cachedTextGenerator.lines.Count - 1;
			}
			else
			{
				int remainingGlobalLines = globalLineNumber - topLineNumber;
				foreach( Text _entry in activeEntries )
				{
					EnsureEntryTextGeneratorUpToDate( _entry );
					IList<UILineInfo> lineInfos = _entry.cachedTextGenerator.lines;
					for( int i = 0; i < lineInfos.Count; i++ )
					{
						// Ignore wrapped lines
						int prevChar = lineInfos[i].startCharIdx - 1;
						if( prevChar >= 0 && _entry.text[prevChar] == '\n' )
							remainingGlobalLines--;

						if( remainingGlobalLines == 0 )
						{
							entry = _entry;
							return i;
						}
					}

					remainingGlobalLines--;
				}

				Debug.LogErrorFormat( "Couldn't convert global line number {0} to local line number ({1})", globalLineNumber, remainingGlobalLines );
				entry = null;
				return -1;
			}
		}

		/// <summary>
		/// Normally, texts outside RectMask2D aren't updated until they become visible. We can force update them to make
		/// sure that their <see cref="Text.cachedTextGenerator"/> data is up-to-date.
		/// </summary>
		private void EnsureEntryTextGeneratorUpToDate( Text entry )
		{
			if( entry.canvasRenderer.cull )
				entry.cachedTextGenerator.PopulateWithErrors( entry.text, entry.GetGenerationSettings( entry.rectTransform.rect.size ), entry.gameObject );
		}
	}
}