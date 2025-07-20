class TabsModel:
    """
    Model for default and ground item tabs.
    """
    def __init__(self, available_items: list[str] | None = None):
        items = available_items or []
        self.default_items = items.copy()
        self.ground_items = []
        self.current_tab = 'default'
        # legacy compatibility
        self.available_items = items
