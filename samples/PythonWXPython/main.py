import logging
import wx

import velopack
from _build_config import update_url

class BufferedLogHandler(logging.Handler):
    """Buffers log messages and provides access to them"""
    def __init__(self):
        super().__init__()
        self.buffer = []
    
    def emit(self, record):
        msg = self.format(record)
        self.buffer.append(msg)
    
    def get_messages(self):
        """Get all buffered messages and clear the buffer"""
        messages = self.buffer[:]
        self.buffer.clear()
        return messages

# Global buffered handler
log_handler = BufferedLogHandler()

class MainFrame(wx.Frame):
    def __init__(self):
        super().__init__(None, title="Velopack Sample", size=(600, 400))

        # create velopacks update manager
        self.update_manager = None
        self.update_info = None
        
        # Create main panel
        panel = wx.Panel(self)
        
        # Create buttons
        self.check_btn = wx.Button(panel, label="Check")
        self.download_btn = wx.Button(panel, label="Download")
        self.apply_btn = wx.Button(panel, label="Apply")
        
        # Create log text control (multiline and read-only)
        self.log_text = wx.TextCtrl(panel, 
                                   style=wx.TE_MULTILINE | wx.TE_READONLY)
        
        # Bind button events
        self.check_btn.Bind(wx.EVT_BUTTON, self.on_check)
        self.download_btn.Bind(wx.EVT_BUTTON, self.on_download)
        self.apply_btn.Bind(wx.EVT_BUTTON, self.on_apply)
        
        # Layout
        self.setup_layout(panel)
        
        # Center the frame
        self.Center()
        
        # Start timer to check for log messages
        self.timer = wx.Timer(self)
        self.Bind(wx.EVT_TIMER, self.on_timer)
        self.timer.Start(5)
        
        logging.info("GUI initialized and ready")

    def on_timer(self, event):
        """Check for new log messages and add them to text control"""
        messages = log_handler.get_messages()
        for msg in messages:
            self.log_text.AppendText(f"{msg}\n")

    def setup_layout(self, panel):
        # Create sizers
        main_sizer = wx.BoxSizer(wx.VERTICAL)
        button_sizer = wx.BoxSizer(wx.HORIZONTAL)
        
        # Add buttons to horizontal sizer
        button_sizer.Add(self.check_btn, 0, wx.ALL, 5)
        button_sizer.Add(self.download_btn, 0, wx.ALL, 5)
        button_sizer.Add(self.apply_btn, 0, wx.ALL, 5)
        
        # Add to main sizer
        main_sizer.Add(button_sizer, 0, wx.ALL | wx.EXPAND, 10)
        main_sizer.Add(self.log_text, 1, wx.ALL | wx.EXPAND, 10)
        
        panel.SetSizer(main_sizer)
    
    def on_check(self, event):
        try:
            self.update_manager = velopack.UpdateManager(update_url)
        except Exception as e:
            logging.error(f"Failed to initialize update manager: {e}")
            return
        self.update_info = self.update_manager.check_for_updates()
        if self.update_info:
            logging.info(f"Update available: {self.update_info}")
        else:
            logging.info("No updates available")

    
    def on_download(self, event):
        if not self.update_info:
            logging.warning("No update information available. Please check first.")
            return
        
        try:
            self.update_manager.download_updates(self.update_info)
            logging.info("Update downloaded successfully")
        except Exception as e:
            logging.error(f"Failed to download update: {e}")
    
    def on_apply(self, event):
        if not self.update_info:
            logging.warning("No update information available. Please check first.")
            return
        
        try:
            self.update_manager.apply_updates_and_restart(self.update_info)
            logging.info("Update applied successfully")
        except Exception as e:
            logging.error(f"Failed to apply update: {e}")

def setup_logging():
    """Setup logging to use buffered handler"""
    logger = logging.getLogger()
    
    # Remove existing handlers
    for handler in logger.handlers[:]:
        logger.removeHandler(handler)
    
    # Setup our buffered handler
    formatter = logging.Formatter('- %(levelname)s - %(message)s')
    log_handler.setFormatter(formatter)
    log_handler.setLevel(logging.INFO)
    
    logger.addHandler(log_handler)
    logger.setLevel(logging.INFO)

if __name__ == "__main__":
    # Setup logging before velopack runs
    setup_logging()
    
    # Run velopack early
    velopack.App().run()
    
    # Create and run the wx app
    app = wx.App(False)
    frame = MainFrame()
    frame.Show()
    app.MainLoop()