class ListModel:
    """
    Model for scroll state and selection in the item list.
    """
    def __init__(self, visible_count: int = 10):
        self.visible_count = visible_count
        self.scroll_offset = 0
        self.selected_item = None
        self.selected_index = None
