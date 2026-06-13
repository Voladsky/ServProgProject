// froglogic.js

class FrogGame {
    constructor() {
        this.connection = null;
        this.gameId = null;
        this.playerToken = null;
        this.playerName = '';
        this.currentTurnToken = null;
        this.board = [];
        this.isPlayer1 = false;
        this.players = {};
        this.myRemoved = false;
        this.myFirstTurn = true;
        this.state = 'lobby';
        this.removeMode = false;

        this.selectedSource = null;
        this.jumpChain = [];
        this.availableTargets = new Set();
    }

    async connectAndJoin(name, gameIdInput) {
        this.playerName = name;
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl('/gamehub')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Information)
            .build();

        this.registerHandlers();

        try {
            await this.connection.start();
        } catch (err) {
            this.setStatus('error', 'Connection failed: ' + err);
            showModal();
            return;
        }

        try {
            if (!gameIdInput || gameIdInput.trim() === '') {
                await this.connection.invoke('CreateGame');
            } else {
                await this.connection.invoke('JoinGame', gameIdInput.trim());
            }
        } catch (err) {
            this.setStatus('error', err.message || 'Failed to create/join game');
            showModal();
            return;
        }

        hideModal();
        this.setStatus('waiting-join', 'Waiting for opponent...');
    }

    async reconnectFromStorage() {
        const gid = sessionStorage.getItem('fg_gameId');
        const tok = sessionStorage.getItem('fg_playerToken');
        const name = sessionStorage.getItem('fg_playerName');
        if (!gid || !tok) return false;

        this.playerName = name || 'Player';
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl('/gamehub', {
                gameId: gid,
                playerToken: tok
            })
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(signalR.LogLevel.Information)
            .build();

        this.registerHandlers();
        try {
            await this.connection.start();
            this.gameId = gid;
            this.playerToken = tok;
            hideModal();
            return true;
        } catch (err) {
            sessionStorage.clear();
            return false;
        }
    }

    registerHandlers() {
        this.connection.on('GameCreated', (gameId, playerToken) => {
            this.gameId = gameId;
            this.playerToken = playerToken;
            this.isPlayer1 = true;
            this.persistState();
            window.showGameId(gameId);
        });

        this.connection.on('JoinedGame', (playerToken) => {
            this.playerToken = playerToken;
            this.isPlayer1 = false;
            this.persistState();
        });

        this.connection.on('JoinFailed', (message) => {
            this.setStatus('error', message);
            showModal();
        });

        this.connection.on('GameStarted', (data) => {
            this.currentTurnToken = data.currentTurn;
            this.board = data.board.cells ? data.board.cells : data.board;
            this.players[data.player1] = this.isPlayer1 ? this.playerName : 'Opponent';
            this.players[data.player2] = this.isPlayer1 ? 'Opponent' : this.playerName;
            this.myRemoved = this.isPlayer1 ? data.player1Removed : data.player2Removed;
            this.myFirstTurn = true;
            this.updateBoardView();
            this.updateGameStateAfterServerUpdate();
            this.persistState();
        });

        this.connection.on('FrogRemoved', (row, col, playerToken) => {
            if (this.board[row] && this.board[row][col] !== undefined) {
                this.board[row][col] = 0;
            }
            if (playerToken === this.playerToken) {
                this.myRemoved = true;
            }
            this.updateBoardView();
            this.updateGameStateAfterServerUpdate();
        });

        this.connection.on('MoveMade', (data) => {
            this.board = data.board.cells ? data.board.cells : data.board;
            this.updateBoardView();
            this.clearSelection();
            if (data.madeBy === this.playerToken) {
                this.myFirstTurn = false;
            }
        });

        this.connection.on('TurnChanged', (currentTurnToken) => {
            this.currentTurnToken = currentTurnToken;
            this.updateGameStateAfterServerUpdate();
        });

        this.connection.on('GameOver', (winnerToken, reason) => {
            this.state = 'gameover';
            const winnerName = this.players[winnerToken] || 'Someone';
            this.setStatus('gameover', `${winnerName} wins! ${reason}`);
        });

        this.connection.on('PlayerDisconnected', (playerToken) => {
            if (playerToken !== this.playerToken) {
                this.setStatus('their-turn', 'Opponent disconnected, waiting for reconnect...');
            }
        });

        this.connection.on('PlayerReconnected', (playerToken) => {
            if (playerToken !== this.playerToken) {
                this.setStatus(this.state === 'your-turn' ? 'your-turn' : 'their-turn', 'Opponent reconnected');
            }
        });

        this.connection.onclose(async () => {
            this.setStatus('error', 'Disconnected. Trying to reconnect...');
        });
    }

    persistState() {
        if (this.gameId && this.playerToken) {
            sessionStorage.setItem('fg_gameId', this.gameId);
            sessionStorage.setItem('fg_playerToken', this.playerToken);
            sessionStorage.setItem('fg_playerName', this.playerName);
        }
    }

    updateGameStateAfterServerUpdate() {
        if (this.state === 'gameover') return;
        const myTurn = (this.currentTurnToken === this.playerToken);
        if (!myTurn) {
            this.state = 'their-turn';
            const oppName = this.isPlayer1
                ? this.players[Object.keys(this.players).find(t => t !== this.playerToken)]
                : this.players[Object.keys(this.players).find(t => t !== this.playerToken)];
            this.setStatus('their-turn', `${oppName || 'Opponent'}'s turn`);
            return;
        }

        const canRemove = this.myFirstTurn && !this.myRemoved;
        if (!this.hasAnyLegalMove()) {
            this.state = 'your-turn';
            this.setStatus('your-turn',
                canRemove ? 'No jumps. You may remove a frog or pass.' : 'No jumps available. Pass.',
                true);
        } else {
            this.state = 'your-turn';
            this.setStatus('your-turn',
                canRemove ? 'Select frog to jump, or remove a frog (optional).' : 'Select frog to jump.');
        }
        window.setRemoveButtonVisible(canRemove);
        this.clearSelection();
    }

    hasAnyLegalMove() {
        const playerPiece = this.isPlayer1 ? 1 : 2;
        for (let r = 0; r < 8; r++) {
            for (let c = 0; c < 8; c++) {
                if (this.board[r] && this.board[r][c] === playerPiece) {
                    if (getLegalJumps(this.board, r, c).length > 0) return true;
                }
            }
        }
        return false;
    }

    handleCellClick(row, col) {
        if (this.state === 'gameover' || this.state === 'lobby' || this.state === 'waiting-join') return;
        if (row < 0 || row > 7 || col < 0 || col > 7) return;

        if (this.removeMode) {
            this.connection.invoke('RemoveFrog', this.gameId, row, col)
                .catch(err => this.setStatus('error', err.message));
            this.removeMode = false;
            window.setRemoveMode(false);
            return;
        }

        if (this.state === 'your-turn') {
            if (!this.selectedSource) {
                const piece = this.board[row][col];
                if (piece === (this.isPlayer1 ? 1 : 2)) {
                    this.selectedSource = { r: row, c: col };
                    this.jumpChain = [];
                    this.showAvailableTargets(row, col);
                }
                return;
            }

            const targetKey = `${row},${col}`;
            if (this.availableTargets.has(targetKey)) {
                this.jumpChain.push({ r: row, c: col });
                const tempBoard = this.board.map(arr => [...arr]);
                let curR = this.selectedSource.r, curC = this.selectedSource.c;
                for (const dest of this.jumpChain) {
                    const mr = curR + (dest.r - curR) / 2;
                    const mc = curC + (dest.c - curC) / 2;
                    tempBoard[mr][mc] = 0;
                    tempBoard[curR][curC] = 0;
                    tempBoard[dest.r][dest.c] = this.isPlayer1 ? 1 : 2;
                    curR = dest.r; curC = dest.c;
                }
                const nextJumps = getLegalJumps(tempBoard, curR, curC);
                if (nextJumps.length === 0) {
                    this.finishMove();
                } else {
                    this.selectedSource = { r: curR, c: curC };
                    this.showAvailableTargets(curR, curC);
                }
            } else {
                const piece = this.board[row][col];
                if (piece === (this.isPlayer1 ? 1 : 2)) {
                    this.selectedSource = { r: row, c: col };
                    this.jumpChain = [];
                    this.showAvailableTargets(row, col);
                } else {
                    this.clearSelection();
                }
            }
        }
    }

    showAvailableTargets(r, c) {
        const jumps = getLegalJumps(this.board, r, c);
        this.availableTargets = new Set(jumps.map(j => `${j.r},${j.c}`));
        window.highlightCells(jumps);
    }

    clearSelection() {
        this.selectedSource = null;
        this.jumpChain = [];
        this.availableTargets.clear();
        window.clearHighlights();
    }

    finishMove() {
        if (!this.selectedSource || this.jumpChain.length === 0) return;
        const start = this.selectedSource;
        this.connection.invoke('MakeMove', this.gameId, start.r, start.c, this.jumpChain)
            .catch(err => {
                this.setStatus('error', err.message);
                this.clearSelection();
                this.updateGameStateAfterServerUpdate();
            });
        this.clearSelection();
    }

    passTurn() {
        if (this.state !== 'your-turn') return;
        this.connection.invoke('PassTurn', this.gameId).catch(err => {
            this.setStatus('error', err.message);
        });
    }

    updateBoardView() {
        const cells = [];
        for (let r = 0; r < 8; r++) {
            for (let c = 0; c < 8; c++) {
                const val = this.board[r]?.[c];
                if (val === 1) cells.push({ col: c, row: r, color: 'green' });
                else if (val === 2) cells.push({ col: c, row: r, color: 'orange' });
            }
        }
        window.setBoardState(cells);
    }

    setStatus(stateClass, message, passEnabled = false) {
        const turnMap = {
            'lobby': '🐸 LOBBY',
            'waiting-join': '⏳ Waiting for opponent',
            'your-turn': '🐸 YOUR TURN',
            'their-turn': '⏳ THEIR TURN',
            'gameover': '🏆 Game Over',
            'error': '⚠️ ERROR',
        };
        const players = [];
        if (this.isPlayer1) {
            players.push({ name: this.playerName || 'You', active: this.currentTurnToken === this.playerToken });
            players.push({ name: this.players[Object.keys(this.players).find(t => t !== this.playerToken)] || 'Opponent', active: this.currentTurnToken !== this.playerToken });
        } else if (this.playerToken) {
            players.push({ name: this.players[Object.keys(this.players).find(t => t !== this.playerToken)] || 'Opponent', active: this.currentTurnToken !== this.playerToken });
            players.push({ name: this.playerName || 'You', active: this.currentTurnToken === this.playerToken });
        }
        updateStatus({
            state: stateClass,
            turn: turnMap[stateClass] || '🐸',
            players: players.length ? players : [{ name: '—', active: false }],
            msg: message,
            passEnabled: passEnabled
        });
    }

    toggleRemoveMode() {
        if (!this.myFirstTurn || this.myRemoved) return;
        this.removeMode = !this.removeMode;
        window.setRemoveMode(this.removeMode);
        this.clearSelection();
        if (this.removeMode) {
            this.setStatus('your-turn', 'Click any cell to remove a frog');
        } else {
            this.updateGameStateAfterServerUpdate();
        }
    }
}

// ─── Rules‑compliant jump detection (matches backend: white = inner 6x6) ───
function getLegalJumps(board, r, c, simulatedBoard = null) {
    const currentBoard = simulatedBoard || board;
    const jumps = [];

    for (let dr = -1; dr <= 1; dr++) {
        for (let dc = -1; dc <= 1; dc++) {
            if (dr === 0 && dc === 0) continue;
            const midR = r + dr, midC = c + dc;
            const landR = r + 2 * dr, landC = c + 2 * dc;

            if (midR < 0 || midR > 7 || midC < 0 || midC > 7) continue;
            if (landR < 0 || landR > 7 || landC < 0 || landC > 7) continue;

            if (currentBoard[midR][midC] === 0) continue;
            if (currentBoard[landR][landC] !== 0) continue;

            // White squares: rows 1-6 and cols 1-6 (inner board)
            const isWhite = (landR >= 1 && landR <= 6 && landC >= 1 && landC <= 6);
            if (isWhite) {
                jumps.push({ r: landR, c: landC, isSwamp: false });
            } else {
                // Swamp jump – only legal if there is at least one further jump
                const tempBoard = copyBoard(currentBoard);
                tempBoard[midR][midC] = 0;
                tempBoard[landR][landC] = tempBoard[r][c];
                tempBoard[r][c] = 0;
                const nextJumps = getLegalJumps(tempBoard, landR, landC);
                if (nextJumps.length > 0) {
                    jumps.push({ r: landR, c: landC, isSwamp: true });
                }
            }
        }
    }
    return jumps;
}

function copyBoard(board) {
    return board.map(row => [...row]);
}

// ─── Global helpers ──────────────────────────────────────────
function showModal() {
    document.getElementById('joinModalBackdrop').style.display = 'flex';
}
function hideModal() {
    document.getElementById('joinModalBackdrop').style.display = 'none';
}

function updateStatus({ state = '', turn = '', players = [], msg = '', passEnabled = false } = {}) {
    const panel = document.querySelector('.status-panel');
    const turnEl = document.getElementById('turnText');
    const playersEl = document.getElementById('playersList');
    const msgEl = document.getElementById('gameMsg');
    const passBtn = document.getElementById('passBtn');

    panel.classList.remove('state-error', 'state-gameover');
    if (state) panel.classList.add('state-' + state);

    turnEl.textContent = turn;
    if (Array.isArray(players)) {
        playersEl.innerHTML = players.map(p =>
            `<span class="player-badge${p.active ? ' active' : ''}">${p.name}</span>`
        ).join('');
    } else {
        playersEl.innerHTML = players;
    }
    msgEl.textContent = msg;
    if (passBtn) passBtn.disabled = !passEnabled;
}

window.setRemoveButtonVisible = function (visible) {
    const btn = document.getElementById('removeBtn');
    if (btn) btn.style.display = visible ? 'inline-block' : 'none';
};

window.setRemoveMode = function (active) {
    const btn = document.getElementById('removeBtn');
    if (btn) {
        btn.classList.toggle('ring-2', active);
        btn.classList.toggle('ring-swamp-600', active);
    }
};

window.showGameId = function (gameId) {
    const block = document.getElementById('gameIdDisplay');
    if (block) {
        block.style.display = 'flex';
        document.getElementById('gameIdText').textContent = gameId;
    }
};

window.copyGameId = function () {
    const text = document.getElementById('gameIdText').textContent;
    navigator.clipboard.writeText(text).then(() => {
        const btn = document.getElementById('copyBtn');
        btn.textContent = 'Copied!';
        setTimeout(() => { btn.textContent = 'Copy'; }, 1500);
    });
};

// Initialize game
window.frogGame = new FrogGame();