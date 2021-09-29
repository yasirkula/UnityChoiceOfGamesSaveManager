using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CoGSaveManager
{
	// Run this script before anything else
	[DefaultExecutionOrder( -1000 )]
	public class ErrorReporter : MonoBehaviour
	{
#pragma warning disable 0649
		[SerializeField]
		private GameObject errorPanel;

		[SerializeField]
		private InputField errorText;
		private string displayedError;

		[SerializeField]
		private Button ignoreErrorButton, closeButton;
#pragma warning restore 0649

		private readonly Queue<string> queuedErrors = new Queue<string>( 16 );
		private readonly HashSet<string> ignoredErrors = new HashSet<string>();
		private readonly object queueLock = new object();

		private void Awake()
		{
			Application.logMessageReceivedThreaded -= OnLogReceived;
			Application.logMessageReceivedThreaded += OnLogReceived;

			ignoreErrorButton.onClick.AddListener( () =>
			{
				if( !string.IsNullOrEmpty( displayedError ) )
					ignoredErrors.Add( displayedError );

				errorPanel.gameObject.SetActive( false );
			} );

			closeButton.onClick.AddListener( () => errorPanel.gameObject.SetActive( false ) );
		}

		private void OnLogReceived( string logString, string stackTrace, LogType logType )
		{
			if( logType == LogType.Error || logType == LogType.Exception )
			{
				string errorMessage = string.Concat( logString, "\n", stackTrace );
				if( !ignoredErrors.Contains( errorMessage ) )
				{
					lock( queueLock )
						queuedErrors.Enqueue( errorMessage );
				}
			}
		}

		private void LateUpdate()
		{
			if( queuedErrors.Count > 0 && !errorPanel.gameObject.activeSelf )
			{
				string error;
				lock( queueLock )
					error = queuedErrors.Dequeue();

				if( ignoredErrors.Contains( error ) )
					return;

				errorText.text = displayedError = error;
				errorPanel.gameObject.SetActive( true );
			}
		}
	}
}