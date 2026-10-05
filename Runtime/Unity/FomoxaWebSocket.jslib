var FomoxaWebSocketLibrary = {
  $FomoxaSockets: { next: 1, table: {} },

  FomoxaWsOpen: function (urlPointer) {
    var url = UTF8ToString(urlPointer);
    var id = FomoxaSockets.next++;
    var entry = { socket: null, queue: [], state: 0 };
    FomoxaSockets.table[id] = entry;
    try {
      entry.socket = new WebSocket(url);
    } catch (error) {
      entry.state = 4;
      return id;
    }

    entry.socket.binaryType = 'arraybuffer';
    entry.socket.onopen = function () {
      if (entry.state === 0) {
        entry.state = 1;
      }
    };
    entry.socket.onmessage = function (event) {
      if (typeof event.data === 'string') {
        entry.state = 4;
        entry.socket.close();
        return;
      }

      entry.queue.push(new Uint8Array(event.data));
    };
    entry.socket.onerror = function () {
      entry.state = 4;
    };
    entry.socket.onclose = function (event) {
      if (entry.state !== 4) {
        entry.state = event.wasClean ? 3 : 4;
      }
    };
    return id;
  },

  FomoxaWsState: function (id) {
    var entry = FomoxaSockets.table[id];
    return entry ? entry.state : 4;
  },

  FomoxaWsBufferedAmount: function (id) {
    var entry = FomoxaSockets.table[id];
    return entry && entry.socket ? entry.socket.bufferedAmount : 0;
  },

  FomoxaWsSend: function (id, pointer, length) {
    var entry = FomoxaSockets.table[id];
    if (entry && entry.state === 1) {
      entry.socket.send(HEAPU8.slice(pointer, pointer + length));
    }
  },

  FomoxaWsNextLength: function (id) {
    var entry = FomoxaSockets.table[id];
    return entry && entry.queue.length > 0 ? entry.queue[0].length : -1;
  },

  FomoxaWsReceive: function (id, pointer, capacity) {
    var entry = FomoxaSockets.table[id];
    if (!entry || entry.queue.length === 0 || entry.queue[0].length > capacity) {
      return -1;
    }

    var message = entry.queue.shift();
    HEAPU8.set(message, pointer);
    return message.length;
  },

  FomoxaWsClose: function (id) {
    var entry = FomoxaSockets.table[id];
    if (entry && entry.state === 1) {
      entry.state = 2;
      entry.socket.close(1000);
    }
  },

  FomoxaWsRelease: function (id) {
    var entry = FomoxaSockets.table[id];
    if (!entry) {
      return;
    }

    if (entry.socket && entry.state <= 2) {
      entry.socket.onclose = null;
      entry.socket.close(1000);
    }

    delete FomoxaSockets.table[id];
  }
};

autoAddDeps(FomoxaWebSocketLibrary, '$FomoxaSockets');
mergeInto(LibraryManager.library, FomoxaWebSocketLibrary);
