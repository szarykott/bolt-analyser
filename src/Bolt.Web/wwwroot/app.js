// Render every not-yet-rendered plotly figure delivered as JSON blocks.
function renderCharts() {
  document.querySelectorAll('script[type="application/json"][data-plotly-target]').forEach(function (el) {
    var target = document.getElementById(el.dataset.plotlyTarget);
    if (target && !target.dataset.rendered) {
      var fig = JSON.parse(el.textContent);
      Plotly.newPlot(target, fig.data, fig.layout);
      target.dataset.rendered = "true";
    }
  });
}

// Build a standalone interactive HTML file from the delivered report.
// Chart JSON blocks travel with it; charts re-render on load, so the file
// keeps working after the server-side job is long gone.
function downloadReport() {
  var clone = document.getElementById('report-content').cloneNode(true);
  clone.querySelectorAll('[data-rendered]').forEach(function (d) {
    d.innerHTML = '';
    d.removeAttribute('data-rendered');
  });
  var styleEl = document.querySelector('style');
  var html = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>Bolt ride analysis</title>'
    + '<script src="https://cdn.plot.ly/plotly-2.32.0.min.js"><\/script>'
    + '<style>' + (styleEl ? styleEl.textContent : '') + '</style></head><body>'
    + clone.outerHTML
    + '<script>window.addEventListener("load", ' + renderCharts.toString() + ');<\/script>'
    + '</body></html>';
  var blob = new Blob([html], { type: 'text/html' });
  var a = document.createElement('a');
  a.href = URL.createObjectURL(blob);
  a.download = 'bolt-report.html';
  a.click();
  URL.revokeObjectURL(a.href);
}

document.addEventListener('htmx:wsAfterMessage', renderCharts);
