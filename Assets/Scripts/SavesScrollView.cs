using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	// A recycled list view that creates SaveEntryVisuals for only the visible save entries
	public class SavesScrollView : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private SaveEntryVisual saveVisualPrefab;

		[SerializeField]
		private RectTransform viewportTransform;
		[SerializeField]
		private RectTransform contentTransform;
#pragma warning restore 0649

		private DynamicCircularBuffer<SaveEntry> saves;

		private float saveVisualHeight, _1OverSaveVisualHeight;
		private float viewportHeight;
		private bool viewportHeightDirty;

		private readonly Dictionary<int, SaveEntryVisual> saveVisuals = new Dictionary<int, SaveEntryVisual>();
		private readonly Stack<SaveEntryVisual> pooledSaveVisuals = new Stack<SaveEntryVisual>();

		// Current indices of save visuals shown on screen
		private int currentTopIndex = -1, currentBottomIndex = -1;

		public OnSaveVisualClickedHandler OnSaveVisualClicked;

		public void Initialize( DynamicCircularBuffer<SaveEntry> saves )
		{
			this.saves = saves;

			viewportHeight = viewportTransform.rect.height;
			saveVisualHeight = ( (RectTransform) saveVisualPrefab.transform ).sizeDelta.y;
			_1OverSaveVisualHeight = 1f / saveVisualHeight;

			viewportTransform.parent.GetComponent<ScrollRect>().onValueChanged.AddListener( ( pos ) => UpdateSaveVisualsInTheList( false ) );
		}

		private void OnRectTransformDimensionsChange()
		{
			if( saves != null )
				viewportHeightDirty = true;
		}

		private void LateUpdate()
		{
			// Window is resized, update the list
			if( viewportHeightDirty )
			{
				viewportHeightDirty = false;
				viewportHeight = viewportTransform.rect.height;
				UpdateSaveVisualsInTheList( false );
			}
		}

		// Update the list
		public void UpdateList()
		{
			float newHeight = Mathf.Max( 1f, saves.Count * saveVisualHeight );
			contentTransform.sizeDelta = new Vector2( 0f, newHeight );
			viewportHeight = viewportTransform.rect.height;

			UpdateSaveVisualsInTheList( true );
		}

		// Calculate the indices of save visuals to show
		private void UpdateSaveVisualsInTheList( bool updateAllVisibleSaveVisuals )
		{
			// If there is at least one save to show
			if( saves.Count > 0 )
			{
				float contentPos = contentTransform.anchoredPosition.y - 1f;

				int newTopIndex = (int) ( contentPos * _1OverSaveVisualHeight );
				int newBottomIndex = (int) ( ( contentPos + viewportHeight + 2f ) * _1OverSaveVisualHeight );

				if( newTopIndex < 0 )
					newTopIndex = 0;

				if( newBottomIndex > saves.Count - 1 )
					newBottomIndex = saves.Count - 1;

				if( currentTopIndex == -1 )
				{
					// There are no saves

					updateAllVisibleSaveVisuals = true;

					currentTopIndex = newTopIndex;
					currentBottomIndex = newBottomIndex;

					CreateSaveVisualsBetweenIndices( newTopIndex, newBottomIndex );
				}
				else
				{
					// There are some saves

					if( newBottomIndex < currentTopIndex || newTopIndex > currentBottomIndex )
					{
						// If user scrolled a lot such that, none of the save visuals are now within
						// the bounds of the scroll view, pool all the previous save visuals and create
						// new save visuals for the new list of visible entries
						updateAllVisibleSaveVisuals = true;

						DestroySaveVisualsBetweenIndices( currentTopIndex, currentBottomIndex );
						CreateSaveVisualsBetweenIndices( newTopIndex, newBottomIndex );
					}
					else
					{
						// User did not scroll a lot such that, some save visuals are are still within
						// the bounds of the scroll view. Don't destroy them but update their content,
						// if necessary
						if( newTopIndex > currentTopIndex )
							DestroySaveVisualsBetweenIndices( currentTopIndex, newTopIndex - 1 );
						if( newBottomIndex < currentBottomIndex )
							DestroySaveVisualsBetweenIndices( newBottomIndex + 1, currentBottomIndex );

						if( newTopIndex < currentTopIndex )
						{
							CreateSaveVisualsBetweenIndices( newTopIndex, currentTopIndex - 1 );

							// If it is not necessary to update all the save visuals,
							// then just update the newly created save visuals. Otherwise,
							// wait for the major update
							if( !updateAllVisibleSaveVisuals )
								UpdateSaveVisualContentsBetweenIndices( newTopIndex, currentTopIndex - 1 );
						}

						if( newBottomIndex > currentBottomIndex )
						{
							CreateSaveVisualsBetweenIndices( currentBottomIndex + 1, newBottomIndex );

							// Same as above
							if( !updateAllVisibleSaveVisuals )
								UpdateSaveVisualContentsBetweenIndices( currentBottomIndex + 1, newBottomIndex );
						}
					}

					currentTopIndex = newTopIndex;
					currentBottomIndex = newBottomIndex;
				}

				if( updateAllVisibleSaveVisuals )
				{
					// Update all save visual
					UpdateSaveVisualContentsBetweenIndices( currentTopIndex, currentBottomIndex );
				}
			}
			else if( currentTopIndex != -1 )
			{
				// There is nothing to show but some save visuals are still visible; pool them
				DestroySaveVisualsBetweenIndices( currentTopIndex, currentBottomIndex );
				currentTopIndex = -1;
			}
		}

		private void CreateSaveVisualsBetweenIndices( int topIndex, int bottomIndex )
		{
			for( int i = topIndex; i <= bottomIndex; i++ )
				CreateSaveVisualAtIndex( i );
		}

		// Create (or unpool) a save visual
		private void CreateSaveVisualAtIndex( int index )
		{
			SaveEntryVisual saveVisual;
			if( pooledSaveVisuals.Count > 0 )
			{
				saveVisual = pooledSaveVisuals.Pop();
				saveVisual.gameObject.SetActive( true );
			}
			else
			{
				saveVisual = (SaveEntryVisual) Instantiate( saveVisualPrefab, contentTransform, false );
				saveVisual.transform.SetParent( contentTransform, false );
				saveVisual.listView = this;
			}

			// Reposition the save visual
			( (RectTransform) saveVisual.transform ).anchoredPosition = new Vector2( 0f, -index * saveVisualHeight );

			// To access this save visual easily in the future, add it to the dictionary
			saveVisuals[index] = saveVisual;
		}

		private void DestroySaveVisualsBetweenIndices( int topIndex, int bottomIndex )
		{
			for( int i = topIndex; i <= bottomIndex; i++ )
			{
				SaveEntryVisual saveVisual = saveVisuals[i];

				saveVisual.gameObject.SetActive( false );
				pooledSaveVisuals.Push( saveVisual );
			}
		}

		private void UpdateSaveVisualContentsBetweenIndices( int topIndex, int bottomIndex )
		{
			for( int i = topIndex; i <= bottomIndex; i++ )
			{
				SaveEntryVisual saveVisual = saveVisuals[i];

				saveVisual.Position = i;
				saveVisual.SetContent( saves[saves.Count - 1 - saveVisual.Position] ); // Show saves in descending order
			}
		}
	}
}