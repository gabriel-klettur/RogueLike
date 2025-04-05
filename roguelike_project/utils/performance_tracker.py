import time
from contextlib import contextmanager

class PerformanceTracker:
    def __init__(self, sample_size=60):
        self.sample_size = sample_size
        self.performance_log = {
            'handle_events': [],
            'update': [],
            'render': [],
            'frame_times': []
        }
        
    def __enter__(self):
        self.start_time = time.perf_counter()
        return self
        
    def track_frame(self):
        """Call this at the end of each frame to track timing"""
        self.frame_time = time.perf_counter() - self.start_time
        self.performance_log['frame_times'].append(self.frame_time)
        self._print_stats_if_needed()
        self.start_time = time.perf_counter()  # Reset for next frame

    def __exit__(self, exc_type, exc_val, exc_tb):
        return False
        
    @contextmanager
    def time_section(self, section_name):
        start_time = time.perf_counter()
        try:
            yield
        finally:
            elapsed = time.perf_counter() - start_time
            self.performance_log[section_name].append(elapsed)
    
    def _print_stats_if_needed(self):
        if len(self.performance_log['frame_times']) >= self.sample_size:
            avg_frame = sum(self.performance_log['frame_times']) / self.sample_size
            avg_fps = 1 / avg_frame if avg_frame > 0 else 0
            print(f"\nPerformance (Last {self.sample_size} frames):")
            print(f"  FPS: {avg_fps:.1f} (Target: {FPS})")
            print(f"  Events: {sum(self.performance_log['handle_events'])/self.sample_size:.4f}s/frame")
            print(f"  Update: {sum(self.performance_log['update'])/self.sample_size:.4f}s/frame")
            print(f"  Render: {sum(self.performance_log['render'])/self.sample_size:.4f}s/frame")
            print("-" * 40)  # Add separator for better visibility
            
            # Reset tracking but keep last few samples for smoother transitions
            for key in self.performance_log:
                self.performance_log[key] = self.performance_log[key][-10:]

from roguelike_project.config import FPS

@contextmanager
def nullcontext():
    yield
