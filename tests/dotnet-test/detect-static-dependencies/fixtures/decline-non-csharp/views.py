from datetime import datetime
def index(request):
    now = datetime.now()
    return render(request, 'index.html', {'time': now})
