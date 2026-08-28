p='D:/temp/Rogue_commit_msgs/commit_progress.txt'
try:
    print(open(p).read())
except FileNotFoundError:
    print('NO_PROGRESS_FILE_YET')