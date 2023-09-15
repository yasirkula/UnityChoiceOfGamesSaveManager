using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public delegate void OnSaveEntryModifiedHandler( SaveEntry modifiedSaveEntry );

	// A recycled list view that creates SaveEditorVisuals for only the visible settings
	public class SaveEditorWindow : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Text title;

		[SerializeField]
		private Button saveButton, cancelButton, closeButton;

		[SerializeField]
		private InputField searchInputField;

		[SerializeField]
		private SaveEditorEntryVisual itemPrefab;

		[SerializeField]
		private ScrollRect scrollView;
		[SerializeField]
		private RectTransform viewportTransform;
		[SerializeField]
		private RectTransform contentTransform;
#pragma warning restore 0649

		private SaveEntry saveEntry, lastSaveEntry;
		private string saveFilePath;
		private string saveFilePrefix;
		private JsonSaveData saveData;
		private IList<JsonNode> saveDataVisibleNodes;

		private float itemHeight, _1OverItemHeight;
		private float viewportHeight;
		private bool viewportHeightDirty;

		private readonly Dictionary<JsonNode, string> originalJsonNodeValues = new Dictionary<JsonNode, string>( 1024 );
		private readonly HashSet<JsonNode> modifiedJsonNodes = new HashSet<JsonNode>();

		private readonly Dictionary<int, SaveEditorEntryVisual> items = new Dictionary<int, SaveEditorEntryVisual>();
		private readonly Stack<SaveEditorEntryVisual> pooledItems = new Stack<SaveEditorEntryVisual>();

		private readonly CompareInfo textComparer = new CultureInfo( "en-US" ).CompareInfo;
		private readonly CompareOptions textCompareOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

		// Used to make sure that the scrolled content always remains within the scroll view's boundaries
		private PointerEventData nullPointerEventData;

		// Current indices of items shown on screen
		private int currentTopIndex = -1, currentBottomIndex = -1;

		public OnSaveEntryModifiedHandler OnSaveEntryModified;

		public void Show( SaveEntry saveEntry, string saveFilePath )
		{
			this.saveEntry = saveEntry;
			this.saveFilePath = saveFilePath;
			title.text = string.Concat( "Editing: \"", saveEntry.saveName, "\"" );

			string saveFileContents = File.ReadAllText( saveFilePath );
			int saveFileJsonStartIndex = saveFileContents.IndexOf( '\n' ) + 1;
			saveFilePrefix = saveFileContents.Substring( 0, saveFileJsonStartIndex );
			saveData = new JsonSaveData( saveFileContents, saveFileJsonStartIndex, saveFileContents.Length - 1 );
			saveDataVisibleNodes = saveData.VisibleNodes;

			originalJsonNodeValues.Clear();
			modifiedJsonNodes.Clear();

			foreach( JsonNode node in saveData.VisibleNodes )
				originalJsonNodeValues[node] = node.StringValue;

			searchInputField.text = "";
			OnSaveModifiedStateChanged( false );

			if( lastSaveEntry != saveEntry )
			{
				// Scroll to the top when editing a new save file
				scrollView.verticalNormalizedPosition = 1f;
				lastSaveEntry = saveEntry;
			}

			gameObject.SetActive( true );
			UpdateList();
		}

		private void Awake()
		{
			viewportHeight = viewportTransform.rect.height;
			itemHeight = ( (RectTransform) itemPrefab.transform ).sizeDelta.y;
			_1OverItemHeight = 1f / itemHeight;

			saveButton.onClick.AddListener( () =>
			{
				StringBuilder sb = new StringBuilder( saveFilePrefix.Length + saveData.JsonLength );
				sb.Append( saveFilePrefix );
				saveData.ToJson( sb );

				// Try preserve the save file's 'Last Modified Date' so that we don't create another automated save
				DateTime lastWriteTime = File.GetLastWriteTime( saveFilePath );
				File.WriteAllText( saveFilePath, sb.ToString() );
				File.SetLastWriteTime( saveFilePath, lastWriteTime );

				foreach( JsonNode node in modifiedJsonNodes )
					originalJsonNodeValues[node] = node.StringValue;

				modifiedJsonNodes.Clear();
				OnSaveModifiedStateChanged( false );

				if( OnSaveEntryModified != null )
					OnSaveEntryModified( saveEntry );
			} );

			cancelButton.onClick.AddListener( () => gameObject.SetActive( false ) );
			closeButton.onClick.AddListener( () => gameObject.SetActive( false ) );

			searchInputField.onValueChanged.AddListener( ( value ) =>
			{
				if( gameObject.activeInHierarchy )
				{
					// Filter the visible items using search string
					if( string.IsNullOrEmpty( value ) )
						saveDataVisibleNodes = saveData.VisibleNodes;
					else
					{
						if( !( saveDataVisibleNodes is List<JsonNode> ) )
							saveDataVisibleNodes = new List<JsonNode>( saveData.VisibleNodes.Length );
						else
							saveDataVisibleNodes.Clear();

						foreach( JsonNode node in saveData.VisibleNodes )
						{
							if( textComparer.IndexOf( node.Key, value, textCompareOptions ) >= 0 || textComparer.IndexOf( node.StringValue, value, textCompareOptions ) >= 0 )
								saveDataVisibleNodes.Add( node );
						}
					}

					UpdateList();

					// Ensure scroll view is within bounds
					// When scrollbar is snapped to the very bottom of the scroll view, sometimes OnScroll alone doesn't work
					if( scrollView.normalizedPosition.y <= Mathf.Epsilon )
						scrollView.normalizedPosition = new Vector2( scrollView.normalizedPosition.x, 0.001f );

					scrollView.OnScroll( nullPointerEventData );
				}
			} );

			scrollView.onValueChanged.AddListener( ( pos ) => UpdateItemsInTheList( false ) );

			nullPointerEventData = new PointerEventData( null );
		}

		private void OnRectTransformDimensionsChange()
		{
			viewportHeightDirty = true;
		}

		private void LateUpdate()
		{
			// Window is resized, update the list
			if( viewportHeightDirty && saveData != null )
			{
				viewportHeightDirty = false;
				viewportHeight = viewportTransform.rect.height;
				UpdateItemsInTheList( false );
			}
		}

		private void OnSaveModifiedStateChanged( bool isModified )
		{
			cancelButton.gameObject.SetActive( isModified );
			closeButton.gameObject.SetActive( !isModified );
			saveButton.interactable = isModified;
		}

		internal void OnItemModified( SaveEditorEntryVisual item )
		{
			if( originalJsonNodeValues[item.Data] != item.Data.StringValue )
			{
				if( modifiedJsonNodes.Add( item.Data ) && !saveButton.interactable )
					OnSaveModifiedStateChanged( true );
			}
			else
			{
				if( modifiedJsonNodes.Remove( item.Data ) && modifiedJsonNodes.Count == 0 && saveButton.interactable )
					OnSaveModifiedStateChanged( false );
			}
		}

		// Update the list
		public void UpdateList()
		{
			float newHeight = Mathf.Max( 1f, saveDataVisibleNodes.Count * itemHeight );
			contentTransform.sizeDelta = new Vector2( 0f, newHeight );
			viewportHeight = viewportTransform.rect.height;

			UpdateItemsInTheList( true );
		}

		// Calculate the indices of items to show
		private void UpdateItemsInTheList( bool updateAllVisibleItems )
		{
			// If there is at least one save to show
			if( saveDataVisibleNodes.Count > 0 )
			{
				float contentPos = contentTransform.anchoredPosition.y - 1f;

				int newTopIndex = (int) ( contentPos * _1OverItemHeight );
				int newBottomIndex = (int) ( ( contentPos + viewportHeight + 2f ) * _1OverItemHeight );

				if( newTopIndex < 0 )
					newTopIndex = 0;

				if( newBottomIndex > saveDataVisibleNodes.Count - 1 )
					newBottomIndex = saveDataVisibleNodes.Count - 1;

				if( currentTopIndex == -1 )
				{
					// There are no saves

					updateAllVisibleItems = true;

					currentTopIndex = newTopIndex;
					currentBottomIndex = newBottomIndex;

					CreateItemsBetweenIndices( newTopIndex, newBottomIndex );
				}
				else
				{
					// There are some saves

					if( newBottomIndex < currentTopIndex || newTopIndex > currentBottomIndex )
					{
						// If user scrolled a lot such that, none of the items are now within
						// the bounds of the scroll view, pool all the previous items and create
						// new items for the new list of visible entries
						updateAllVisibleItems = true;

						DestroyItemsBetweenIndices( currentTopIndex, currentBottomIndex );
						CreateItemsBetweenIndices( newTopIndex, newBottomIndex );
					}
					else
					{
						// User did not scroll a lot such that, some items are are still within
						// the bounds of the scroll view. Don't destroy them but update their content,
						// if necessary
						if( newTopIndex > currentTopIndex )
							DestroyItemsBetweenIndices( currentTopIndex, newTopIndex - 1 );
						if( newBottomIndex < currentBottomIndex )
							DestroyItemsBetweenIndices( newBottomIndex + 1, currentBottomIndex );

						if( newTopIndex < currentTopIndex )
						{
							CreateItemsBetweenIndices( newTopIndex, currentTopIndex - 1 );

							// If it is not necessary to update all the items,
							// then just update the newly created items. Otherwise,
							// wait for the major update
							if( !updateAllVisibleItems )
								UpdateItemContentsBetweenIndices( newTopIndex, currentTopIndex - 1 );
						}

						if( newBottomIndex > currentBottomIndex )
						{
							CreateItemsBetweenIndices( currentBottomIndex + 1, newBottomIndex );

							// Same as above
							if( !updateAllVisibleItems )
								UpdateItemContentsBetweenIndices( currentBottomIndex + 1, newBottomIndex );
						}
					}

					currentTopIndex = newTopIndex;
					currentBottomIndex = newBottomIndex;
				}

				if( updateAllVisibleItems )
				{
					// Update all item
					UpdateItemContentsBetweenIndices( currentTopIndex, currentBottomIndex );
				}
			}
			else if( currentTopIndex != -1 )
			{
				// There is nothing to show but some items are still visible; pool them
				DestroyItemsBetweenIndices( currentTopIndex, currentBottomIndex );
				currentTopIndex = -1;
			}
		}

		private void CreateItemsBetweenIndices( int topIndex, int bottomIndex )
		{
			for( int i = topIndex; i <= bottomIndex; i++ )
				CreateItemAtIndex( i );
		}

		// Create (or unpool) a item
		private void CreateItemAtIndex( int index )
		{
			SaveEditorEntryVisual item;
			if( pooledItems.Count > 0 )
			{
				item = pooledItems.Pop();
				item.gameObject.SetActive( true );
			}
			else
			{
				item = (SaveEditorEntryVisual) Instantiate( itemPrefab, contentTransform, false );
				item.listView = this;
			}

			// Reposition the item
			( (RectTransform) item.transform ).anchoredPosition = new Vector2( 0f, -index * itemHeight );

			// To access this item easily in the future, add it to the dictionary
			items[index] = item;
		}

		private void DestroyItemsBetweenIndices( int topIndex, int bottomIndex )
		{
			for( int i = topIndex; i <= bottomIndex; i++ )
			{
				SaveEditorEntryVisual item = items[i];

				item.gameObject.SetActive( false );
				pooledItems.Push( item );
			}
		}

		private void UpdateItemContentsBetweenIndices( int topIndex, int bottomIndex )
		{
			for( int i = topIndex; i <= bottomIndex; i++ )
			{
				SaveEditorEntryVisual item = items[i];

				item.Position = i;
				item.SetContent( saveDataVisibleNodes[item.Position] );
			}
		}
	}
}