using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SearchableDropdown : Dropdown
	{
		protected class SearchableDropdownItem : DropdownItem
		{
			public override void OnPointerEnter( PointerEventData eventData )
			{
			}
		}

		private ScrollRect scrollView;
		private InputField searchInputField;
		private readonly List<DropdownItem> items = new List<DropdownItem>( 64 );

		// Used to make sure that the scrolled content always remains within the scroll view's boundaries
		private PointerEventData nullPointerEventData;

		private readonly CompareInfo textComparer = new CultureInfo( "en-US" ).CompareInfo;
		private readonly CompareOptions textCompareOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

		protected override void Awake()
		{
			base.Awake();
			nullPointerEventData = new PointerEventData( null );
		}

		protected override GameObject CreateDropdownList( GameObject template )
		{
			items.Clear();

			/// Use <see cref="SearchableDropdownItem"/> instead of <see cref="Dropdown.DropdownItem"/> on items because the latter changes
			/// keyboard focus from search InputField to hovered items which breaks search input.
			if( itemText.rectTransform.parent.GetComponent<SearchableDropdownItem>() == null )
			{
				DestroyImmediate( itemText.rectTransform.parent.GetComponent<DropdownItem>() );

				SearchableDropdownItem itemTemplate = itemText.rectTransform.parent.gameObject.AddComponent<SearchableDropdownItem>();
				itemTemplate.text = itemText;
				itemTemplate.image = itemImage;
				itemTemplate.toggle = itemTemplate.GetComponent<Toggle>();
				itemTemplate.rectTransform = (RectTransform) itemTemplate.transform;
			}

			GameObject dropdownList = base.CreateDropdownList( template );
			scrollView = dropdownList.GetComponent<ScrollRect>();

			searchInputField = dropdownList.GetComponentInChildren<InputField>( true );
			searchInputField.onValueChanged.AddListener( OnSearchTermChanged );

			return dropdownList;
		}

		/// This is called after all <see cref="SearchableDropdownItem"/>s are created.
		protected override GameObject CreateBlocker( Canvas rootCanvas )
		{
			// Give keyboard focus to the search input field by default
			searchInputField.Select();

			/// Focus <see cref="scrollView"/> on the selected item by default. Credit: https://gist.github.com/yasirkula/75ca350fb83ddcc1558d33a8ecf1483f
			DropdownItem selectedItem = items.Find( ( item ) => item.toggle.isOn );
			if( selectedItem != null )
			{
				Vector2 viewportSize = scrollView.viewport.rect.size;
				Vector2 contentSize = scrollView.content.rect.size;
				Vector2 contentScale = scrollView.content.localScale;
				Vector2 itemCenterPoint = scrollView.content.InverseTransformPoint( selectedItem.rectTransform.TransformPoint( selectedItem.rectTransform.rect.center ) );
				Vector2 focusPoint = itemCenterPoint + Vector2.Scale( contentSize, scrollView.content.pivot );

				contentSize.Scale( contentScale );
				focusPoint.Scale( contentScale );

				float verticalNormalizedPosition = scrollView.verticalNormalizedPosition;
				if( contentSize.y > viewportSize.y )
					verticalNormalizedPosition = Mathf.Clamp01( ( focusPoint.y - viewportSize.y * 0.5f ) / ( contentSize.y - viewportSize.y ) );

				scrollView.verticalNormalizedPosition = verticalNormalizedPosition;
			}

			return base.CreateBlocker( rootCanvas );
		}

		public Vector2 CalculateFocusedScrollPosition( Vector2 focusPoint )
		{
			Vector2 contentSize = scrollView.content.rect.size;
			Vector2 viewportSize = ( (RectTransform) scrollView.content.parent ).rect.size;
			Vector2 contentScale = scrollView.content.localScale;

			contentSize.Scale( contentScale );
			focusPoint.Scale( contentScale );

			Vector2 scrollPosition = scrollView.normalizedPosition;
			if( scrollView.horizontal && contentSize.x > viewportSize.x )
				scrollPosition.x = Mathf.Clamp01( ( focusPoint.x - viewportSize.x * 0.5f ) / ( contentSize.x - viewportSize.x ) );
			if( scrollView.vertical && contentSize.y > viewportSize.y )
				scrollPosition.y = Mathf.Clamp01( ( focusPoint.y - viewportSize.y * 0.5f ) / ( contentSize.y - viewportSize.y ) );

			return scrollPosition;
		}

		protected override DropdownItem CreateItem( DropdownItem itemTemplate )
		{
			DropdownItem item = base.CreateItem( itemTemplate );
			items.Add( item );
			return item;
		}

		private void OnSearchTermChanged( string searchTerm )
		{
			if( items.Count == 0 )
				return;

			Vector2 itemPos = items[items.Count - 1].rectTransform.anchoredPosition;
			Vector2 itemHeight = new Vector2( 0f, items[0].rectTransform.sizeDelta.y );
			float padding = itemPos.y + items[0].rectTransform.rect.min.y;
			int visibleItems = 0;
			for( int i = items.Count - 1; i >= 0; i-- )
			{
				DropdownItem item = items[i];
				bool showItem = textComparer.IndexOf( item.text.text, searchTerm, textCompareOptions ) >= 0;
				if( item.gameObject.activeSelf != showItem )
					item.gameObject.SetActive( showItem );

				if( showItem )
				{
					item.rectTransform.anchoredPosition = itemPos;
					itemPos += itemHeight;
					visibleItems++;
				}
			}

			scrollView.content.sizeDelta = new Vector2( scrollView.content.sizeDelta.x, visibleItems * itemHeight.y + padding * 2f );

			// Ensure scroll view is within bounds
			// When scrollbar is snapped to the very bottom of the scroll view, sometimes OnScroll alone doesn't work
			if( scrollView.normalizedPosition.y <= Mathf.Epsilon )
				scrollView.normalizedPosition = new Vector2( scrollView.normalizedPosition.x, 0.001f );

			scrollView.OnScroll( nullPointerEventData );
		}
	}
}