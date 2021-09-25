using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class SaveEditorEntryVisual : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Image background;

		[SerializeField]
		private Text label;

		[SerializeField]
		private InputField stringInputField;

		[SerializeField]
		private Toggle boolToggle;

		[SerializeField]
		private Button editAsTextButton;

		[SerializeField]
		private Color[] alternatingRowColors;
#pragma warning restore 0649

		internal SaveEditorWindow listView;

		public int Position { get; set; }
		public JsonNode Data { get; private set; }

		private void Awake()
		{
			stringInputField.onEndEdit.AddListener( ( value ) =>
			{
				if( stringInputField.gameObject.activeInHierarchy )
				{
					Data.StringValue = value;
					listView.OnItemModified( this );
				}
			} );

			boolToggle.onValueChanged.AddListener( ( value ) =>
			{
				if( boolToggle.gameObject.activeInHierarchy )
				{
					Data.BooleanValue = value;
					listView.OnItemModified( this );
				}
			} );

			editAsTextButton.onClick.AddListener( () =>
			{
				stringInputField.text = Data.StringValue;

				stringInputField.gameObject.SetActive( true );
				boolToggle.gameObject.SetActive( false );
				editAsTextButton.gameObject.SetActive( false );
			} );
		}

		public void SetContent( JsonNode data )
		{
			Data = data;

			label.text = data.Key;
			background.color = alternatingRowColors[Position % alternatingRowColors.Length];

			// Deactivating input fields before changing their values allows us to skip their onEndEdit/onValueChanged
			// events while setting their initial values below
			stringInputField.gameObject.SetActive( false );
			boolToggle.gameObject.SetActive( false );

			if( data.IsBoolean )
			{
				boolToggle.isOn = Data.BooleanValue;
				boolToggle.gameObject.SetActive( true );
				editAsTextButton.gameObject.SetActive( true );
			}
			else
			{
				stringInputField.text = Data.StringValue;
				stringInputField.gameObject.SetActive( true );
				editAsTextButton.gameObject.SetActive( false );
			}
		}
	}
}