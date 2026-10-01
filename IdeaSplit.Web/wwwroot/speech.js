window.ideaSplitSpeech = (() => {
    const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    let recognition = null;

    return {
        isSupported: () => !!Recognition,

        start(dotNetRef, lang) {
            if (!Recognition) return false;
            this.stop();

            recognition = new Recognition();
            recognition.lang = lang || navigator.language || "en-US";
            recognition.continuous = true;
            recognition.interimResults = true;

            recognition.onresult = (event) => {
                let transcript = "";
                for (let i = 0; i < event.results.length; i++) {
                    transcript += event.results[i][0].transcript;
                }
                dotNetRef.invokeMethodAsync("OnSpeechResult", transcript);
            };
            recognition.onerror = (event) => dotNetRef.invokeMethodAsync("OnSpeechError", event.error || "unknown");
            recognition.onend = () => {
                recognition = null;
                dotNetRef.invokeMethodAsync("OnSpeechEnded");
            };

            try {
                recognition.start();
                return true;
            } catch (e) {
                recognition = null;
                return false;
            }
        },

        stop() {
            if (recognition) {
                try { recognition.stop(); } catch (e) { /* already stopped */ }
            }
        }
    };
})();
