#include <wx/wx.h>
#include <optional>
#include <string>
#include "Velopack.h"

using namespace Velopack;

std::optional<UpdateManager> updateManager;
std::optional<UpdateInfo> updateInfo;
std::string logBuffer;

std::optional<UpdateManager> get_or_create_update_manager()
{
    try
    {
        updateManager = UpdateManager(RELEASES_DIR);
    }
    catch (std::exception& ex)
    {
        std::string message = std::string("\n") + ex.what();
        logBuffer.append(message);
    }
    return updateManager;
}

std::string get_status()
{
    return "";
}

class MyFrame : public wxFrame
{
public:
    MyFrame() : wxFrame(nullptr, wxID_ANY, "VelopackCppWidgetsSample", wxDefaultPosition, wxSize(600, 600))
    {
        // Set background color to white
        SetBackgroundColour(*wxWHITE);

        // Main vertical sizer
        wxBoxSizer* mainSizer = new wxBoxSizer(wxVERTICAL);

        // Auto-wrapping text
        topText = new wxStaticText(this, wxID_ANY,
            "This is a sample text that will automatically wrap based on the width of the window. "
            "Resize the window to see the text wrap around.");
        topText->Wrap(380);  // Set wrap width close to the window width
        mainSizer->Add(topText, 0, wxALL | wxEXPAND, 10);

        // Create a horizontal sizer for the buttons
        wxBoxSizer* buttonSizer = new wxBoxSizer(wxHORIZONTAL);
        wxButton* button1 = new wxButton(this, wxID_ANY, "Check for Updates");
        wxButton* button2 = new wxButton(this, wxID_ANY, "Button 2");
        wxButton* button3 = new wxButton(this, wxID_ANY, "Button 3");

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

        // Set up a timer for periodic updates
        timer = new wxTimer(this);
        Bind(wxEVT_TIMER, &MyFrame::OnTimer, this);
        timer->Start(1000);  // Trigger updates every second
    }

    ~MyFrame()
    {
        timer->Stop();
        delete timer;
    }

private:
    wxStaticText* topText;
    wxTextCtrl* textArea;
    wxTimer* timer;
    int updateCount = 0;

    void OnTimer(wxTimerEvent&)
    {
        // Update the static text
        topText->SetLabel(wxString::Format("Updated Text - %d", updateCount));
        topText->Wrap(380);  // Re-wrap after changing text

        // Update the scrollable text area
        textArea->AppendText(wxString::Format("Log Entry %d\n", updateCount));

        updateCount++;
    }
};

class MyApp : public wxApp
{
public:
    virtual bool OnInit()
    {
        get_or_create_update_manager();
        MyFrame* frame = new MyFrame();
        frame->Show(true);
        return true;
    }
};

wxIMPLEMENT_APP(MyApp);