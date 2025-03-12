#include <wx/wx.h>
#include <optional>
#include <string>
#include <thread>

// Includes for MyExampleUpdateSource
#include <fstream>
#include <sstream>
#include <filesystem>
#include <thread>

#include "Velopack.hpp"

using namespace Velopack;

/**
 * This is just an example of how to create a fully custom update source
 * Normally, you should use one of the built-ins instead, like FileSource or HttpSource.
 */
class MyExampleUpdateSource : public IUpdateSource
{
public:
    const std::string GetReleaseFeed(const std::string releasesName) override
    {
        std::string releasesFile = std::string(RELEASES_DIR) + "\\" + releasesName;
        std::ifstream t(releasesFile);
        std::stringstream buffer;
        buffer << t.rdbuf();
        return buffer.str();
    }
    bool DownloadReleaseEntry(const VelopackAsset& asset, const std::string localFilePath, vpkc_progress_send_t progress) override
    {
        using namespace std::chrono_literals;
        std::string path = std::string(RELEASES_DIR) + "\\" + asset.FileName;
        std::filesystem::copy_file(path, localFilePath);

        // simulate the download taking some time...
        std::this_thread::sleep_for(1s);
        progress(25);
        std::this_thread::sleep_for(1s);
        progress(50);
        std::this_thread::sleep_for(1s);
        progress(75);
        std::this_thread::sleep_for(1s);
        progress(100);

        return true;
    }
};

class MyFrame : public wxFrame
{
public:
    ~MyFrame()
    {
        vpkc_set_logger(nullptr, nullptr);
    }
    MyFrame() : wxFrame(nullptr, wxID_ANY, "VelopackCppWidgetsSample", wxDefaultPosition, wxSize(600, 600))
    {
        vpkc_set_logger(&MyFrame::HandleVpkcLogStatic, this);

        // Set background color to white
        // SetBackgroundColour(*wxWHITE);

        // Main vertical sizer
        wxBoxSizer* mainSizer = new wxBoxSizer(wxVERTICAL);

        // Auto-wrapping text
        topText = new wxStaticText(this, wxID_ANY,
                                   "This is a sample text that will automatically wrap based on the width of the window. "
                                   "Resize the window to see the text wrap around.");
        topText->Wrap(380); // Set wrap width close to the window width
        mainSizer->Add(topText, 0, wxALL | wxEXPAND, 10);

        // Create a horizontal sizer for the buttons
        wxBoxSizer* buttonSizer = new wxBoxSizer(wxHORIZONTAL);
        wxButton* button1 = new wxButton(this, wxID_ANY, "Check for Updates");
        wxButton* button2 = new wxButton(this, wxID_ANY, "Download Update");
        wxButton* button3 = new wxButton(this, wxID_ANY, "Restart & Apply");

        button1->Bind(wxEVT_BUTTON, &MyFrame::OnCheckForUpdates, this);
        button2->Bind(wxEVT_BUTTON, &MyFrame::OnDownloadUpdates, this);
        button3->Bind(wxEVT_BUTTON, &MyFrame::OnApplyUpdates, this);

        // Add buttons to the button sizer
        buttonSizer->Add(button1, 0, wxALL, 5);
        buttonSizer->Add(button2, 0, wxALL, 5);
        buttonSizer->Add(button3, 0, wxALL, 5);

        // Add the button sizer to the main sizer
        mainSizer->Add(buttonSizer, 0, wxALIGN_CENTER);

        // Add a large, scrollable text area
        textArea = new wxTextCtrl(this, wxID_ANY, "", wxDefaultPosition, wxSize(600, 400),
                                  wxTE_MULTILINE | wxTE_RICH2 | wxTE_READONLY | wxTE_AUTO_URL);
        mainSizer->Add(textArea, 1, wxALL | wxEXPAND, 10);

        // Set the sizer for the frame
        SetSizer(mainSizer);
        mainSizer->Fit(this);

        // initialise velopack
        try
        {
            updateManager = std::make_unique<UpdateManager>(std::make_unique<MyExampleUpdateSource>());
            topText->SetLabel("Current Version: " + updateManager->GetCurrentVersion());
        }
        catch (std::exception& ex)
        {
            std::string message = ex.what() + std::string("\n");
            textArea->AppendText(message);
            topText->SetLabel(message);
        }
    }

private:
    wxStaticText* topText;
    wxTextCtrl* textArea;
    std::unique_ptr<UpdateManager> updateManager;
    std::optional<UpdateInfo> updateInfo;
    bool downloaded = false;

    void OnCheckForUpdates(wxCommandEvent& event)
    {
        if (!updateManager)
        {
            textArea->AppendText("Cannot check for updates. Install the app first.\n");
            return;
        }

        downloaded = false;

        try 
        {
            updateInfo = updateManager->CheckForUpdates();
            if (updateInfo.has_value())
            {
                topText->SetLabel("Update Found: " + updateInfo.value().TargetFullRelease.Version);
            }
            else
            {
                topText->SetLabel("No Update Found.");
            }
        }
        catch (...) { /* exception will print in log */ }
    }

    void OnDownloadUpdates(wxCommandEvent& event)
    {
        if (!updateManager || !updateInfo.has_value())
        {
            textArea->AppendText("Cannot download updates. Check for updates first.\n");
            return;
        }

        // start download on new thread
        std::thread([this]()
        {
            try
            {
                updateManager->DownloadUpdates(updateInfo.value(), &MyFrame::HandleProgressCallbackStatic, this);
                downloaded = true;
                wxTheApp->CallAfter([this]()
                {
                    topText->SetLabel("Download Complete.");
                });
            }
            catch (...) { /* exception will print in log */ }
        }).detach();
    }

    void OnApplyUpdates(wxCommandEvent& event)
    {
        if (!updateManager || !downloaded)
        {
            textArea->AppendText("Cannot apply updates. Download updates first.\n");
            return;
        }

        try
        {
            updateManager->WaitExitThenApplyUpdates(updateInfo.value());
            wxTheApp->ExitMainLoop();
        }
        catch (...) { /* exception will print in log */ }

    }

    void HandleVpkcLog(const char* pszLevel, const char* pszMessage)
    {
        std::string level(pszLevel);
        std::string message(pszMessage);
        wxTheApp->CallAfter([this, level, message]() {
            if (textArea) { // Ensure textArea is valid.
                textArea->AppendText(level + ": " + message + "\n");
            }
        });
    }

    static void HandleVpkcLogStatic(void* context, const char* pszLevel, const char* pszMessage)
    {
        MyFrame* instance = static_cast<MyFrame*>(context);
        instance->HandleVpkcLog(pszLevel, pszMessage);
    }

    void HandleProgressCallback(size_t progress)
    {
        wxTheApp->CallAfter([this, progress]()
        {
            topText->SetLabel("Download Progress: " + std::to_string(progress));
        });
    }

    static void HandleProgressCallbackStatic(void* pUserData, size_t progress)
    {
        MyFrame* instance = static_cast<MyFrame*>(pUserData);
        instance->HandleProgressCallback(progress);
    }
};

class MyApp : public wxApp
{
public:
    virtual bool OnInit()
    {
        // Velopack should be the first thing to run in app startup, before any UI
        // has been shown. Velopack may need to quit/restart the application at this point.
        VelopackApp::Build().Run();

        MyFrame* frame = new MyFrame();
        frame->Show(true);
        return true;
    }
};

wxIMPLEMENT_APP(MyApp);
