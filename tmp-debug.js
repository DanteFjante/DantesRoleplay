const { connectedCampaignToHubEnvelope } = await import('./prototype/dnd2024/src/server/connected-hub-envelope.ts');
const envelope = connectedCampaignToHubEnvelope({
  version: 1,
  status: 'connected',
  applicationId: 'dnd2024',
  stateSpaceId: 'dnd2024-main',
  audience: { seat: 'dm', perspective: 'dm', allowedPerspectives: ['dm', 'player'] },
  campaign: { id: 'campaign.thalorien.brackenford', name: 'The Waystone at Brackenford', premise: null, partyGoals: ['Build trust with the people of Brackenford.'], toneAndBoundaries: [] },
  actor: { id: 'orban', name: 'Orban', state: null, entries: [] },
  knowledge: { status: 'ready', entries: [{ text: 'A placeholder campaign fact.', stance: 'known', presentationKind: 'statement' }], locations: [] },
  locationDirectory: [
    { id: 'location.thalorien.brackenford', name: 'Brackenford' },
    { id: 'location.thalorien.crownmere', name: 'Crownmere' },
    { id: 'location.thalorien.southwestern-volcanic-region', name: 'Southwestern Volcanic Region' },
  ],
});
console.log(envelope.world.locations.map((l) => l.region));
console.log(envelope.world.regions);
