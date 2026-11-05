function showDeleteModal() {
    console.log("Show delete modal clicked");
    const modal = document.querySelector('.modal-overlay');
    if (modal) {
        modal.style.display = 'flex';
    }
}

function hideDeleteModal() {
    console.log("Hide delete modal clicked");
    const modal = document.querySelector('.modal-overlay');
    if (modal) {
        modal.style.display = 'none';
    }
}

async function confirmDeleteMeetup(meetupId) {
    console.log("Confirm delete clicked for meetup:", meetupId);
    
    const deleteBtn = document.getElementById('confirmDeleteBtn');
    if (deleteBtn) {
        deleteBtn.disabled = true;
        deleteBtn.innerHTML = '<span class="loading-spinner" style="width:1rem;height:1rem;margin-right:.5rem;"></span>Deleting…';
    }
    
    // Redirect to meetups page with delete action
    window.location.href = '/meetups?action=delete&id=' + meetupId;
}
