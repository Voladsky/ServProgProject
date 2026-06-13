console.log('froglogic.js loaded');

// Client logic to connect to GameHub and control UI interactions
(function () {
    let connection = null;
    let myToken = null;
    let myGameId = null;
    let mySide = null; // 1 or 2
    let boardState = null; // server-provided board object
    let selected = null;
    let removeMode = false;

    const joinBtn = () => document.getElementById('joinBtn');
    const playerNameInput = () => document.getElementById('playerNameInput');
    const gameIdInput = () => document.getElementById('gameIdInput');
    const turnText = () => document.getElementById('turnText');
    const playersList = () => document.getElementById('playersList');
    const gameMsg = () => document.getElementById('gameMsg');
    const passBtn = () => document.getElementById('passBtn');

    function mapBoardToCells(board) {
        // board is expected to have property 'cells' (8x8 jagged) or 'Cells'
        const raw = board?.cells ?? board?.Cells;
        if (!raw) return [];
        const cells = [];
        for (let r = 1; r <= 6; r++) {
            for (let c = 1; c <= 6; c++) {
                const v = raw[r][c];
                if (v === 1) cells.push({ col: c - 1, row: r - 1, color: 'orange' });
                else if (v === 2) cells.push({ col: c - 1, row: r - 1, color: 'green' });
            }
        }
        return cells;
    }

    function updateUIFromGamePayload(payload) {
        boardState = payload.board ?? payload;
        window.setBoardState(mapBoardToCells(boardState));
        // players
        const p1 = payload.player1;
        const p2 = payload.player2;
        const players = [];
        if (p1) players.push({ name: p1 === myToken ? 'You' : p1.substring(0,6), active: payload.currentTurn === p1 });
        if (p2) players.push({ name: p2 === myToken ? 'You' : p2.substring(0,6), active: payload.currentTurn === p2 });
        playersList().innerHTML = players.map(p => `<span class="player-badge${p.active ? ' active' : ''}">${p.name}</span>`).join('');
        turnText().textContent = payload.currentTurn === myToken ? '🐸 YOUR TURN' : (payload.currentTurn ? payload.currentTurn.substring(0,6) : '—');
        gameMsg().textContent = 'Game in progress';
    }

    function getCell(board, r, c) {
        const raw = board?.cells ?? board?.Cells;
        if (!raw) return 0;
        return raw[r][c];
    }

    function inBounds(r,c){ return r>=0 && r<8 && c>=0 && c<8; }

    function getLegalJumps(board, r, c) {
        const res = [];
        for (let dr=-1; dr<=1; dr++) for (let dc=-1; dc<=1; dc++){
            if (dr===0 && dc===0) continue;
            const mr = r + dr, mc = c + dc;
            const tr = r + 2*dr, tc = c + 2*dc;
            if (!inBounds(mr,mc) || !inBounds(tr,tc)) continue;
            if (getCell(board,mr,mc)===0) continue;
            if (getCell(board,tr,tc)!==0) continue;
            res.push({r:tr, c:tc});
        }
        return res;
    }

    async function ensureConnection() {
        if (connection && connection.state === signalR.HubConnectionState.Connected) return connection;
        connection = new signalR.HubConnectionBuilder().withUrl('/gamehub').build();

        connection.on('GameCreated', (gameId, token) => {
            myGameId = gameId;
            myToken = token;
            gameMsg().textContent = `Created ${gameId}`;
            // show players
            playersList().innerHTML = `<span class="player-badge active">You</span>`;
            // display id in input for sharing
            gameIdInput().value = gameId;
            // assign side: creator is player1
            mySide = 1;
        });

        connection.on('JoinFailed', (reason) => {
            alert('Join failed: ' + reason);
            gameMsg().textContent = 'Join failed';
        });

        connection.on('JoinedGame', (token) => {
            myToken = token;
            gameMsg().textContent = 'Joined game';
        });

        connection.on('GameStarted', (payload) => {
            // payload: { board, currentTurn, player1, player2, ... }
            updateUIFromGamePayload(payload);
            // determine my side
            if (payload.player1 && myToken === payload.player1) mySide = 1;
            else if (payload.player2 && myToken === payload.player2) mySide = 2;
            myGameId = payload.player1 ? (payload.player1 === myToken ? payload.player1 : myGameId) : myGameId;
            // reveal pass button when needed
        });

        connection.on('FrogRemoved', (r,c,by) => {
            gameMsg().textContent = `Frog removed ${r},${c}`;
            // server does not include board here; rely on MoveMade or GameStarted to update board
        });

        connection.on('MoveMade', (payload) => {
            boardState = payload.board;
            window.setBoardState(mapBoardToCells(boardState));
            gameMsg().textContent = `Move by ${payload.madeBy ? (payload.madeBy===myToken ? 'You' : payload.madeBy.substring(0,6)) : 'opponent'}`;
        });

        connection.on('TurnChanged', (token) => {
            turnText().textContent = token === myToken ? '🐸 YOUR TURN' : token ? token.substring(0,6) : '—';
        });

        connection.on('PlayerDisconnected', (id) => { gameMsg().textContent = 'Player disconnected'; });
        connection.on('PlayerReconnected', (id) => { gameMsg().textContent = 'Player reconnected'; });
        connection.on('GameOver', (winner, reason) => { alert('Game over: ' + (winner===myToken ? 'You' : (winner ? winner.substring(0,6) : 'Unknown')) + ' - ' + reason); gameMsg().textContent = 'Game over'; });

        try {
            await connection.start();
            console.log('SignalR connected');
        } catch (e) {
            console.error('Failed to start SignalR', e);
            alert('Failed to connect to server');
        }
        return connection;
    }

    // Canvas click handling — translate clicks to board coords and send actions
    function attachCanvasHandlers() {
        const parent = document.getElementById('gameCanvasParent');
        const observer = new MutationObserver(() => {
            const canvas = parent.querySelector('canvas');
            if (!canvas) return;
            // Once canvas available, attach handler and disconnect observer
            observer.disconnect();
            canvas.addEventListener('pointerdown', async (ev) => {
                if (!boardState) return;
                const rect = canvas.getBoundingClientRect();
                const scaleX = canvas.width / rect.width;
                const scaleY = canvas.height / rect.height;
                const x = (ev.clientX - rect.left) * scaleX;
                const y = (ev.clientY - rect.top) * scaleY;
                const CELL = 65;
                const col = Math.floor(x / CELL);
                const row = Math.floor(y / CELL);
                // clicked board coords (0..7)
                if (!inBounds(row,col)) return;

                if (removeMode) {
                    // send RemoveFrog with clicked coords
                    if (!myGameId) return alert('No game id');
                    try {
                        await connection.invoke('RemoveFrog', myGameId, row, col);
                        gameMsg().textContent = `Removed frog at ${row},${col}`;
                    } catch (e) { alert('Remove failed: ' + (e.message||e)); }
                    removeMode = false;
                    return;
                }

                const st = getCell(boardState, row, col);
                const isOwn = (st === 1 && mySide === 1) || (st === 2 && mySide === 2);
                const turnIsMe = (turnText().textContent === '🐸 YOUR TURN');

                if (!selected) {
                    if (!turnIsMe) { gameMsg().textContent = 'Not your turn'; return; }
                    if (!isOwn) { gameMsg().textContent = 'Not your frog'; return; }
                    selected = { r: row, c: col };
                    gameMsg().textContent = `Selected ${row},${col}`;
                    // highlight could be drawn by Phaser later; skip for brevity
                    return;
                }

                // If already selected, check if clicked is legal destination
                const legal = getLegalJumps(boardState, selected.r, selected.c);
                const clickedIsLegal = legal.some(d => d.r === row && d.c === col);
                if (clickedIsLegal) {
                    try {
                        await connection.invoke('MakeMove', myGameId, selected.r, selected.c, [{ r: row, c: col }]);
                        gameMsg().textContent = `Move sent ${selected.r},${selected.c} -> ${row},${col}`;
                    } catch (e) {
                        alert('Move failed: ' + (e.message||e));
                    }
                } else {
                    gameMsg().textContent = 'Destination not legal';
                }
                selected = null;
            });
        });
        observer.observe(parent, { childList: true, subtree: true });
    }

    // UI handlers
    document.addEventListener('DOMContentLoaded', () => {
        attachCanvasHandlers();
        // join button: create or join
        const jb = document.getElementById('joinBtn');
        jb.addEventListener('click', async (e) => {
            e.preventDefault();
            await ensureConnection();
            const gid = gameIdInput().value && gameIdInput().value.trim();
            if (!gid) {
                // create
                try {
                    await connection.invoke('CreateGame');
                } catch (ex) { alert('Create failed: ' + (ex.message||ex)); }
            } else {
                try {
                    await connection.invoke('JoinGame', gid);
                } catch (ex) { alert('Join failed: ' + (ex.message||ex)); }
            }
        });

        // Remove button
        const rem = document.getElementById('remove');
        if (rem) rem.addEventListener('click', () => { removeMode = true; gameMsg().textContent = 'Click a cell to remove frog'; });

        // Pass button
        if (passBtn()) passBtn().addEventListener('click', async () => {
            if (!myGameId) return alert('Not in game');
            try { await connection.invoke('PassTurn', myGameId); } catch (e) { alert('Pass failed: ' + (e.message||e)); }
        });
    });

    // Expose for debugging
    window._frogClient = {
        ensureConnection,
        getState: () => ({ myToken, myGameId, mySide, boardState })
    };

})();
