let currentSessionId = null;
let currentSize = 0;

function log(message, type = 'info') {
    const logDiv = document.getElementById('log');
    const timestamp = new Date().toLocaleTimeString();
    const colors = { info: '#00ff00', success: '#00ff00', error: '#ff4444', warning: '#ffff00' };
    logDiv.innerHTML += `<div style="color: ${colors[type] || colors.info}">[${timestamp}] ${message}</div>`;
    logDiv.scrollTop = logDiv.scrollHeight;
}

function clearLog() { document.getElementById('log').innerHTML = ''; }

async function startWorker(port) {
    try {
        log(`Запуск воркера на порту ${port}...`, 'info');
        const response = await fetch(`/api/workers/start/${port}`, { method: 'POST' });
        const result = await response.json();
        if (result.success) {
            log(`✅ Воркер запущен (PID: ${result.pid})`, 'success');
            updateWorkersList();
        } else { log(`❌ Ошибка: ${result.error}`, 'error'); }
    } catch (error) { log(`❌ Ошибка: ${error.message}`, 'error'); }
}

async function stopAllWorkers() {
    try {
        log('Остановка всех воркеров...', 'warning');
        const response = await fetch('/api/workers/stop-all', { method: 'POST' });
        const result = await response.json();
        if (result.success) {
            log(`✅ Остановлено воркеров: ${result.stopped}`, 'success');
            updateWorkersList();
        }
    } catch (error) { log(`❌ Ошибка: ${error.message}`, 'error'); }
}

async function updateWorkersList() {
    try {
        const response = await fetch('/api/workers');
        const workers = await response.json();
        const activeCount = workers.filter(w => w.status === 'running').length;
        document.getElementById('activeWorkers').textContent = activeCount;
        const workersDiv = document.getElementById('workersList');
        if (workers.length === 0) {
            workersDiv.innerHTML = '<p class="text-muted">Нет активных воркеров</p>';
        } else {
            workersDiv.innerHTML = workers.map(w => 
                `<div class="d-flex justify-content-between align-items-center p-2 border-bottom">
                    <span><i class="bi bi-pc-display"></i> :${w.port} (PID: ${w.pid})</span>
                    <span class="${w.status === 'running' ? 'text-success' : 'text-danger'}">${w.status === 'running' ? '● Активен' : '● Остановлен'}</span>
                </div>`
            ).join('');
        }
    } catch (error) { console.error('Error:', error); }
}

async function generateSystem() {
    const size = parseInt(document.getElementById('matrixSize').value);
    const seed = document.getElementById('seed').value ? parseInt(document.getElementById('seed').value) : null;
    
    if (size < 2 || size > 5000) {
        log(`❌ Размер должен быть от 2 до 5000`, 'error');
        return;
    }
    
    showLoading(true, `Генерация системы ${size}x${size}...`);
    
    try {
        log(`Генерация системы ${size}x${size}...`, 'info');
        const response = await fetch('/api/generate', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ size, seed })
        });
        const result = await response.json();
        
        if (result.success) {
            currentSessionId = result.sessionId;
            currentSize = result.size;
            
            document.getElementById('systemInfo').innerHTML = `
                <i class='bi bi-check-circle'></i> Система <b>${result.size}x${result.size}</b> сгенерирована<br>
                <small>ID: ${result.sessionId}</small>
            `;
            log(`✅ Система ${result.size}x${result.size} сгенерирована`, 'success');
        } else { 
            log(`❌ Ошибка: ${result.error}`, 'error'); 
        }
    } catch (error) { 
        log(`❌ Ошибка: ${error.message}`, 'error'); 
    }
    showLoading(false);
}

function clearSystem() {
    currentSessionId = null;
    currentSize = 0;
    document.getElementById('systemInfo').innerHTML = '<i class="bi bi-info-circle"></i> Система не загружена';
    document.getElementById('matrixPreview').innerHTML = '';
    document.getElementById('results').innerHTML = '';
    log('Система очищена', 'info');
}

async function solveSequential() {
    if (!currentSessionId) { log('❌ Сначала сгенерируйте систему', 'error'); return; }
    showLoading(true, `Последовательное решение ${currentSize}x${currentSize}...`);
    
    try {
        log(`Запуск последовательного решения ${currentSize}x${currentSize}...`, 'info');
        const response = await fetch('/api/solve/sequential', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionId: currentSessionId })
        });
        const result = await response.json();
        
        if (result.success) {
            log(`✅ Последовательное решение за ${result.timeMs} мс, невязка: ${result.residual.toExponential(2)}`, 'success');
            displayResult('Последовательное', result);
        } else { 
            log(`❌ Ошибка: ${result.error}`, 'error'); 
        }
    } catch (error) { 
        log(`❌ Ошибка: ${error.message}`, 'error'); 
    }
    showLoading(false);
}

async function solveDistributed() {
    if (!currentSessionId) { log('❌ Сначала сгенерируйте систему', 'error'); return; }
    const activeWorkers = parseInt(document.getElementById('activeWorkers').textContent);
    if (activeWorkers === 0) { log('❌ Нет активных воркеров', 'error'); return; }
    
    showLoading(true, `Распределённое решение ${currentSize}x${currentSize}...`);
    
    try {
        log(`Запуск распределённого решения (${activeWorkers} воркеров)...`, 'info');
        const response = await fetch('/api/solve/distributed', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ sessionId: currentSessionId })
        });
        const result = await response.json();
        
        if (result.success) {
            log(`✅ Распределённое решение за ${result.timeMs} мс, невязка: ${result.residual.toExponential(2)}`, 'success');
            displayResult('Распределённое', result);
        } else { 
            log(`❌ Ошибка: ${result.error}`, 'error'); 
        }
    } catch (error) { 
        log(`❌ Ошибка: ${error.message}`, 'error'); 
    }
    showLoading(false);
}

async function compareMethods() {
    if (!currentSessionId && currentSize === 0) { log('❌ Сначала сгенерируйте систему', 'error'); return; }
    const activeWorkers = parseInt(document.getElementById('activeWorkers').textContent);
    if (activeWorkers === 0) { log('❌ Нет активных воркеров', 'error'); return; }
    
    const size = currentSize > 0 ? currentSize : 100;
    showLoading(true, `Сравнение методов для ${size}x${size}...`);
    
    try {
        log('Сравнение методов...', 'info');
        const response = await fetch(`/api/solve/compare?size=${size}&seed=42`, {
            method: 'POST'
        });
        const result = await response.json();
        
        if (result.success) {
            log(`✅ Сравнение завершено. Ускорение: ${result.comparison.speedup.toFixed(4)}x`, 'success');
            displayComparison(result);
        } else { 
            log(`❌ Ошибка: ${result.error}`, 'error'); 
        }
    } catch (error) { 
        log(`❌ Ошибка: ${error.message}`, 'error'); 
    }
    showLoading(false);
}

function showLoading(show, text = 'Загрузка...') {
    document.getElementById('loading').style.display = show ? 'block' : 'none';
    document.getElementById('loadingText').textContent = text;
    ['btnSequential', 'btnDistributed', 'compareMethods'].forEach(id => {
        const btn = document.getElementById(id);
        if (btn) btn.disabled = show;
    });
}

function displayResult(method, result) {
    const resultsDiv = document.getElementById('results');
    const residualClass = result.residual < 1e-6 ? 'text-success' : 'text-danger';
    
    let solutionText = '';
    if (result.solutionFull) {
        solutionText = `<p><b>Решение:</b> [${result.solutionFull.join(', ')}]</p>`;
    } else if (result.solutionPreview) {
        solutionText = `<p><b>Первые 10 значений:</b> [${result.solutionPreview.join(', ')}${result.systemSize > 10 ? '...' : ''}]</p>`;
    }
    
    resultsDiv.innerHTML = `
        <div class="result-box">
            <h5><i class="bi bi-${method === 'Последовательное' ? 'cpu' : 'hdd-network'}"></i> ${method} решение</h5>
            <p><b>Время:</b> ${result.timeMs} мс</p>
            <p><b>Невязка:</b> <span class="${residualClass}">${result.residual.toExponential(6)}</span></p>
            <p><b>Размер:</b> ${result.systemSize}x${result.systemSize}</p>
            ${result.workersCount ? `<p><b>Воркеров:</b> ${result.workersCount}</p>` : ''}
            ${solutionText}
        </div>`;
}

function displayComparison(result) {
    const resultsDiv = document.getElementById('results');
    const speedupClass = result.comparison.speedup > 1 ? 'text-success' : 'text-danger';
    
    resultsDiv.innerHTML = `
        <div class="result-box">
            <h5><i class="bi bi-graph-up"></i> Сравнение методов (${result.systemSize}x${result.systemSize})</h5>
            <div class="row">
                <div class="col-md-6">
                    <h6>Последовательное</h6>
                    <p>Время: <b>${result.sequential.timeMs} мс</b></p>
                    <p>Невязка: <b>${result.sequential.residual.toExponential(6)}</b></p>
                </div>
                <div class="col-md-6">
                    <h6>Распределённое (${result.distributed.workersCount} воркеров)</h6>
                    <p>Время: <b>${result.distributed.timeMs} мс</b></p>
                    <p>Невязка: <b>${result.distributed.residual.toExponential(6)}</b></p>
                </div>
            </div>
            <hr>
            <p><b>Ускорение:</b> <span class="${speedupClass}">${result.comparison.speedup.toFixed(2)}x</span></p>
            <p><b>Макс. разница:</b> ${result.comparison.maxDifference.toExponential(6)}</p>
        </div>`;
}

async function loadHistory() {
    try {
        const response = await fetch('/api/history');
        const history = await response.json();
        const historyDiv = document.getElementById('history');
        if (history.length === 0) {
            historyDiv.innerHTML = '<p class="text-muted">История пуста</p>';
        } else {
            historyDiv.innerHTML = history.map(h => 
                `<div class="p-2 border-bottom"><small>
                    ${h.method === 'sequential' ? '🔵' : '🟢'} 
                    ${h.size}x${h.size}: ${h.timeMs} мс, невязка: ${h.residual.toExponential(2)}
                </small></div>`
            ).join('');
        }
    } catch (error) { console.error('Error:', error); }
}

async function clearHistory() {
    await fetch('/api/history', { method: 'DELETE' });
    loadHistory();
    log('История очищена', 'info');
}

setInterval(updateWorkersList, 2000);
setInterval(loadHistory, 5000);
updateWorkersList();
loadHistory();
log('Приложение запущено. Поддерживаются матрицы до 5000×5000', 'success');
