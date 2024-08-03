using System;
using System.Collections;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using UnityEngine;
using UnityEngine.UI;
using Velopack;
using ILogger = UnityEngine.ILogger;

public class UpdateSample : MonoBehaviour
{
    public Button checkForUpdatesButton;
    public Button downloadUpdatesButton;
    public Button applyUpdatesButton;
    public TMPro.TextMeshProUGUI infoText;
    public TMPro.TMP_InputField inputField;
    
    private UpdateManager m_UpdateManager;
    public int downloadProgress;
    public UpdateInfo NewVersion;
    private void Start()
    {
        if (checkForUpdatesButton)
        {
             checkForUpdatesButton.onClick.AddListener(CheckUpdate);
        }

        if (downloadUpdatesButton)
        {
             downloadUpdatesButton.onClick.AddListener(DownloadUpdate);
        }

        if (applyUpdatesButton)
        {
             applyUpdatesButton.onClick.AddListener(ApplyUpdate);
        }
    }

    private async void CheckUpdate()
    {
        var logger = new UpdateLogger();
        logger.LogAction += (message) => infoText.text = message;
        m_UpdateManager = new UpdateManager(inputField.text,logger: logger);
        NewVersion = await m_UpdateManager.CheckForUpdatesAsync();
    }
    private async void DownloadUpdate()
    {
        if(NewVersion == null)
        {
            infoText.text = "No new version found";
            return;
        }
        checkForUpdatesButton.interactable = false;
        downloadUpdatesButton.interactable = false;
        applyUpdatesButton.interactable = false;
        
        await m_UpdateManager.DownloadUpdatesAsync(NewVersion, i =>
        {
            downloadProgress = i;
            infoText.text = $"Downloading {i}%";
        }).ConfigureAwait(true);
        checkForUpdatesButton.interactable = true;
        downloadUpdatesButton.interactable = true;
        applyUpdatesButton.interactable = true;
    }
    private void ApplyUpdate()
    {
        if (NewVersion == null)
        {
            return;
        }
        //do not use m_UpdateManager.ApplyUpdatesAndExit(NewVersion); here, it has Environment.Exit(0) which will cause the Unity freeze
        m_UpdateManager.WaitExitThenApplyUpdates(NewVersion);
        Application.Quit();
    }
    
    private class UpdateLogger : Microsoft.Extensions.Logging.ILogger
    {
        public Action<string> LogAction;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var message = formatter(state, exception);

            switch(logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Information:
                    Debug.Log(message);
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    Debug.LogError(message);
                    break;
                case LogLevel.None:
                default:
                    return;
            }
            LogAction?.Invoke(message);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }
    }

}
