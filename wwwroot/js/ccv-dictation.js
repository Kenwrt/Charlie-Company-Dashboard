window.ccvDictation = (() => {
    let recognition = null;
    let dotNetReference = null;
    let active = false;
    let activeElementId = null;

    const notify = (isActive, error = null) => {
        active = isActive;
        if (dotNetReference) {
            dotNetReference.invokeMethodAsync(
                "OnDictationStateChanged",
                activeElementId,
                isActive,
                error);
        }
        if (!isActive) {
            activeElementId = null;
        }
    };

    const stop = () => {
        if (recognition && active) {
            recognition.stop();
        }
        active = false;
    };

    const toggle = (elementId, reference) => {
        const Recognition = window.SpeechRecognition || window.webkitSpeechRecognition;
        if (!Recognition) {
            return { supported: false, active: false };
        }

        dotNetReference = reference;
        if (recognition && active) {
            recognition.stop();
            return { supported: true, active: false };
        }

        const textArea = document.getElementById(elementId);
        if (!textArea) {
            return { supported: true, active: false };
        }

        const startingText = textArea.value.trimEnd();
        activeElementId = elementId;
        recognition = new Recognition();
        recognition.continuous = true;
        recognition.interimResults = true;
        recognition.lang = document.documentElement.lang || "en-US";

        recognition.onstart = () => notify(true);
        recognition.onresult = event => {
            let transcript = "";
            for (let index = 0; index < event.results.length; index++) {
                transcript += event.results[index][0].transcript;
            }

            const separator = startingText.length > 0 && transcript.length > 0 ? " " : "";
            textArea.value = `${startingText}${separator}${transcript}`.slice(0, 2000);
            textArea.dispatchEvent(new Event("input", { bubbles: true }));
        };
        recognition.onerror = event => {
            const error = event.error === "not-allowed"
                ? "Microphone access was not allowed. Enable microphone access in the browser and try again."
                : `Dictation stopped: ${event.error}.`;
            notify(false, error);
        };
        recognition.onend = () => notify(false);
        recognition.start();
        active = true;
        return { supported: true, active: true };
    };

    return { toggle, stop };
})();
