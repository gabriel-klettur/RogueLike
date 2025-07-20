#!/usr/bin/env python3
"""
Test runner for left panel components.
Run all tests with: python run_tests.py
Run specific test file with: python run_tests.py test_panel_model.py
"""

import sys
import os
import pytest

# Add the src directory to the Python path
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..', '..', '..', '..', 'src'))

def main():
    """Run the tests."""
    if len(sys.argv) > 1:
        # Run specific test file
        test_file = sys.argv[1]
        if not test_file.startswith('test_'):
            test_file = f'test_{test_file}'
        if not test_file.endswith('.py'):
            test_file = f'{test_file}.py'
        
        test_path = os.path.join(os.path.dirname(__file__), test_file)
        if os.path.exists(test_path):
            pytest.main([test_path, '-v'])
        else:
            print(f"Test file not found: {test_path}")
            return 1
    else:
        # Run all tests in this directory
        test_dir = os.path.dirname(__file__)
        pytest.main([test_dir, '-v'])
    
    return 0

if __name__ == "__main__":
    exit(main())
