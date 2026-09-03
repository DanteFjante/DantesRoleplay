import { useEffect, useRef } from "react";

export function PlayConversationPanel({
  applicationId,
  stateSpaceId,
  sessionContextId,
  onConversationChange,
}: {
  applicationId: string;
  stateSpaceId: string;
  sessionContextId: string;
  onConversationChange: () => void;
}) {
  const container = useRef<HTMLDivElement | null>(null);
  const changeHandler = useRef(onConversationChange);

  useEffect(() => {
    changeHandler.current = onConversationChange;
  }, [onConversationChange]);

  useEffect(() => {
    const conversationModuleUrl = "/components/application-conversation.js";
    void import(/* @vite-ignore */ conversationModuleUrl);
    const parent = container.current;
    if (!parent) return undefined;
    const conversation = document.createElement("application-conversation");
    conversation.setAttribute("application-id", applicationId);
    conversation.setAttribute("state-space-id", stateSpaceId);
    conversation.setAttribute("session-context-id", sessionContextId);
    const changed = () => changeHandler.current();
    conversation.addEventListener("conversation-change", changed);
    parent.replaceChildren(conversation);
    return () => {
      conversation.removeEventListener("conversation-change", changed);
      conversation.remove();
    };
  }, [applicationId, sessionContextId, stateSpaceId]);

  return (
    <section className="play-conversation-panel" aria-labelledby="play-conversation-title">
      <header className="play-conversation-panel__header">
        <span className="eyebrow">Shared play session</span>
        <h2 id="play-conversation-title">Continue the story</h2>
        <p>Your exact words, the Game AI's reply, the active situation, and established truths are saved for this campaign.</p>
      </header>
      {/** The generic element owns play orchestration; this wrapper supplies only the bound identities. */}
      <div ref={container} />
    </section>
  );
}
