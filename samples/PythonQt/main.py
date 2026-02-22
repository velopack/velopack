import logging
import sys
from PySide6.QtWidgets import QApplication, QMainWindow, QWidget, QPushButton, QTextEdit, QVBoxLayout, QHBoxLayout
from PySide6.QtCore import QTimer

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

class MainWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Velopack Sample")
        self.resize(600, 400)

        # create velopacks update manager
        self.update_manager = None
        self.update_info = None
        
        # Create central widget and main layout
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        
        # Create buttons
        self.check_btn = QPushButton("Check")
        self.download_btn = QPushButton("Download")
        self.apply_btn = QPushButton("Apply")
        
        # Create log text control (multiline and read-only)
        self.log_text = QTextEdit()
        self.log_text.setReadOnly(True)
        
        # Connect button events
        self.check_btn.clicked.connect(self.on_check)
        self.download_btn.clicked.connect(self.on_download)
        self.apply_btn.clicked.connect(self.on_apply)
        
        # Layout
        self.setup_layout(central_widget)
        
        # Start timer to check for log messages
        self.timer = QTimer(self)
        self.timer.timeout.connect(self.on_timer)
        self.timer.start(5)  # 5ms interval
        
        logging.info("GUI initialized and ready")

    def on_timer(self):
        """Check for new log messages and add them to text control"""
        messages = log_handler.get_messages()
        for msg in messages:
            self.log_text.append(f"{msg}")

    def setup_layout(self, widget):
        # Create layouts
        main_layout = QVBoxLayout()
        button_layout = QHBoxLayout()
        
        # Add buttons to horizontal layout
        button_layout.addWidget(self.check_btn)
        button_layout.addWidget(self.download_btn)
        button_layout.addWidget(self.apply_btn)
        button_layout.addStretch()  # Add stretch to align buttons to left
        
        # Add to main layout
        main_layout.addLayout(button_layout)
        main_layout.addWidget(self.log_text)
        
        widget.setLayout(main_layout)
    
    def on_check(self):
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

    
    def on_download(self):
        if not self.update_info:
            logging.warning("No update information available. Please check first.")
            return
        
        try:
            self.update_manager.download_updates(self.update_info)
            logging.info("Update downloaded successfully")
        except Exception as e:
            logging.error(f"Failed to download update: {e}")
    
    def on_apply(self):
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
    
    # Create and run the Qt app
    app = QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec())