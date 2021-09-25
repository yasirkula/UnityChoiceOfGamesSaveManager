using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	public class ProgressbarDialog : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private Text text;

		[SerializeField]
		private Slider progressbar;

		[SerializeField]
		private Text progressText;
#pragma warning restore 0649

		public void Show( string text, bool showProgressText )
		{
			this.text.text = text;

			progressbar.value = 0f;
			progressText.gameObject.SetActive( showProgressText );

			gameObject.SetActive( true );
		}

		public void UpdateProgressbar( float progress, string progressLabel )
		{
			progressbar.value = progress;

			if( progressText.gameObject.activeSelf )
				progressText.text = progressLabel;
		}
	}
}